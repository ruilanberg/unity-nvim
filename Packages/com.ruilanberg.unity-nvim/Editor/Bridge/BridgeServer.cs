using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityNvim.Editor.Bridge
{
    /// <summary>
    /// Wire format: one JSON object per line (UTF-8, '\n').
    /// client → {"type":"hello","token":..} then {"id":n,"cmd":"refresh"|"recompile"|"play"|"stop"|"pause"|"status"|"subscribe",...}
    /// server → {"type":"welcome",..} {"type":"response","id":n,"ok":bool,"result"|"error"} {"type":"event","event":..,..}
    /// </summary>
    public static class Protocol
    {
        public const int Version = 1;
    }

    /// <summary>One connected nvim.</summary>
    public sealed class BridgeClient
    {
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly object writeLock = new object();
        volatile bool closed;

        public int Id { get; }
        public bool Authenticated { get; set; }
        public bool WantsLogs { get; set; }
        /// <summary>v:servername of that nvim (for diagnostics only).</summary>
        public string NvimServer { get; set; }
        public DateTime ConnectedAt { get; } = DateTime.Now;
        public bool Closed => closed;

        public BridgeClient(int id, TcpClient tcp)
        {
            Id = id;
            this.tcp = tcp;
            tcp.NoDelay = true;
            stream = tcp.GetStream();
        }

        public void Send(object message)
        {
            if (closed)
                return;
            var bytes = Encoding.UTF8.GetBytes(Json.Serialize(message) + "\n");
            try
            {
                lock (writeLock)
                    stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception)
            {
                Close();
            }
        }

        public void Close()
        {
            if (closed)
                return;
            closed = true;
            try { tcp.Close(); } catch (Exception) { /* ignore */ }
        }

        /// <summary>Blocking read loop (runs on the client's thread).</summary>
        public void ReadLoop(Action<BridgeClient, Dictionary<string, object>> onMessage)
        {
            var buffer = new byte[8192];
            var pending = new List<byte>();
            try
            {
                while (!closed)
                {
                    var n = stream.Read(buffer, 0, buffer.Length);
                    if (n <= 0)
                        break;
                    for (var i = 0; i < n; i++)
                    {
                        if (buffer[i] != (byte)'\n')
                        {
                            pending.Add(buffer[i]);
                            continue;
                        }
                        var line = Encoding.UTF8.GetString(pending.ToArray()).Trim();
                        pending.Clear();
                        if (line.Length == 0)
                            continue;
                        Dictionary<string, object> msg;
                        try
                        {
                            msg = Json.Parse(line) as Dictionary<string, object>;
                        }
                        catch (FormatException)
                        {
                            msg = null;
                        }
                        if (msg != null)
                            onMessage(this, msg);
                    }
                    if (pending.Count > 1 << 20) // 1 MB without newline: not our client
                        break;
                }
            }
            catch (Exception)
            {
                // socket closed (domain reload, nvim quit)
            }
            Close();
        }
    }

    /// <summary>Localhost TCP server that lets nvim drive this editor. Restarted after every domain reload.</summary>
    public static class BridgeServer
    {
        const string PortKey = "UnityNvim.Bridge.Port";
        const string TokenKey = "UnityNvim.Bridge.Token";

        static TcpListener listener;
        static Thread acceptThread;
        static readonly List<BridgeClient> Clients = new List<BridgeClient>();
        static int nextId;

        public static bool Running => listener != null;
        public static int Port { get; private set; }

        public static event Action<BridgeClient> ClientAuthenticated;
        public static event Action ClientsChanged;
        public static event Action<BridgeClient, Dictionary<string, object>> Request;

        public static BridgeClient[] ConnectedClients
        {
            get
            {
                lock (Clients)
                    return Clients.Where(c => c.Authenticated && !c.Closed).ToArray();
            }
        }

        public static void Start()
        {
            if (Running)
                return;
            var token = SessionState.GetString(TokenKey, "");
            if (token == "")
            {
                var bytes = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(bytes);
                token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                SessionState.SetString(TokenKey, token);
            }

            // same port as before the domain reload, so connected nvims come straight back
            var port = SessionState.GetInt(PortKey, 0);
            listener = TryListen(port) ?? TryListen(0);
            if (listener == null)
            {
                Debug.LogWarning("[unity-nvim] bridge: could not open a localhost port");
                return;
            }
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            SessionState.SetInt(PortKey, Port);

            acceptThread = new Thread(() => AcceptLoop(listener, token)) { IsBackground = true, Name = "unity-nvim bridge" };
            acceptThread.Start();
            EndpointFile.Write(Port, token);
        }

        static TcpListener TryListen(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                l.Start();
                return l;
            }
            catch (SocketException)
            {
                return null;
            }
        }

        /// <summary>Close everything. Keeps endpoint.json unless the editor is quitting (a reload brings the same port back).</summary>
        public static void Stop(bool deleteEndpoint)
        {
            var l = listener;
            listener = null;
            try { l?.Stop(); } catch (Exception) { /* ignore */ }
            BridgeClient[] all;
            lock (Clients)
            {
                all = Clients.ToArray();
                Clients.Clear();
            }
            foreach (var c in all)
                c.Close();
            if (deleteEndpoint)
                EndpointFile.Delete();
        }

        static void AcceptLoop(TcpListener l, string token)
        {
            while (true)
            {
                TcpClient tcp;
                try
                {
                    tcp = l.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return; // listener stopped
                }
                var client = new BridgeClient(Interlocked.Increment(ref nextId), tcp);
                lock (Clients)
                    Clients.Add(client);
                new Thread(() =>
                {
                    client.ReadLoop((c, msg) => OnMessage(c, msg, token));
                    bool removed;
                    lock (Clients)
                        removed = Clients.Remove(client);
                    if (removed && client.Authenticated)
                        MainThreadQueue.Enqueue(() => ClientsChanged?.Invoke());
                }) { IsBackground = true, Name = "unity-nvim client " + client.Id }.Start();
            }
        }

        static void OnMessage(BridgeClient client, Dictionary<string, object> msg, string token)
        {
            if (!client.Authenticated)
            {
                if (msg.Str("type") != "hello" || msg.Str("token") != token)
                {
                    client.Send(new Dictionary<string, object> { ["type"] = "error", ["error"] = "bad token" });
                    client.Close();
                    return;
                }
                client.Authenticated = true;
                client.WantsLogs = msg.Bool("logs");
                client.NvimServer = msg.Str("server");
                MainThreadQueue.Enqueue(() =>
                {
                    ClientAuthenticated?.Invoke(client);
                    ClientsChanged?.Invoke();
                });
                return;
            }
            MainThreadQueue.Enqueue(() => Request?.Invoke(client, msg));
        }

        public static void Broadcast(object message, Func<BridgeClient, bool> filter = null)
        {
            foreach (var c in ConnectedClients)
                if (filter == null || filter(c))
                    c.Send(message);
        }

        public static Dictionary<string, object> Event(string name) =>
            new Dictionary<string, object> { ["type"] = "event", ["event"] = name };

        /// <summary>Ask the most recently connected nvim to open a file. False when nobody is connected.</summary>
        public static bool TrySendOpen(string file, int line, int column)
        {
            var target = ConnectedClients.OrderByDescending(c => c.ConnectedAt).FirstOrDefault();
            if (target == null)
                return false;
            var e = Event("open");
            e["file"] = file ?? "";
            e["line"] = line;
            e["col"] = column;
            target.Send(e);
            return !target.Closed;
        }
    }
}

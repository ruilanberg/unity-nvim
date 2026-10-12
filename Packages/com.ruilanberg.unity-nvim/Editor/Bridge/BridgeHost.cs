using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityNvim.Editor.Bridge
{
    /// <summary>
    /// Wires Unity events and requests to the bridge: compile start/finish with messages, play mode, console logs,
    /// and the commands nvim can send. Starts with the editor and after every domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeHost
    {
        const string LastCompileKey = "UnityNvim.Bridge.LastCompile";
        const string ResponsiveKey = "UnityNvim.Bridge.ResponsiveSaved";
        const int MaxLogsPerTick = 200;
        const int Backlog = 200;

        static readonly List<Dictionary<string, object>> CompileMessages = new List<Dictionary<string, object>>();
        static readonly ConcurrentQueue<Dictionary<string, object>> PendingLogs = new ConcurrentQueue<Dictionary<string, object>>();
        static volatile bool anyLogSubscriber;
        static int droppedLogs;

        static BridgeHost()
        {
            if (!EditorProcess.IsMain)
                return;
            ProjectInfo.Capture();
            if (NvimSettings.BridgeEnabled)
                Enable();
            AssemblyReloadEvents.beforeAssemblyReload += () => BridgeServer.Stop(deleteEndpoint: false);
            EditorApplication.quitting += () =>
            {
                RestoreResponsive();
                BridgeServer.Stop(deleteEndpoint: true);
            };
        }

        public static void Enable()
        {
            MainThreadQueue.Hook();
            Subscribe();
            BridgeServer.Start();
        }

        public static void Disable()
        {
            Unsubscribe();
            RestoreResponsive();
            BridgeServer.Stop(deleteEndpoint: true);
        }

        static void Subscribe()
        {
            Unsubscribe();
            BridgeServer.ClientAuthenticated += OnClientAuthenticated;
            BridgeServer.ClientsChanged += OnClientsChanged;
            BridgeServer.Request += OnRequest;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.pauseStateChanged += OnPauseChanged;
            Application.logMessageReceivedThreaded += OnLog;
            MainThreadQueue.Tick += FlushLogs;
        }

        static void Unsubscribe()
        {
            BridgeServer.ClientAuthenticated -= OnClientAuthenticated;
            BridgeServer.ClientsChanged -= OnClientsChanged;
            BridgeServer.Request -= OnRequest;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.pauseStateChanged -= OnPauseChanged;
            Application.logMessageReceivedThreaded -= OnLog;
            MainThreadQueue.Tick -= FlushLogs;
        }

        // ---------------- state ----------------

        static Dictionary<string, object> State() => new Dictionary<string, object>
        {
            ["compiling"] = EditorApplication.isCompiling,
            ["updating"] = EditorApplication.isUpdating,
            ["playing"] = EditorApplication.isPlaying,
            ["paused"] = EditorApplication.isPaused,
        };

        static object LastCompile()
        {
            var raw = SessionState.GetString(LastCompileKey, "");
            return raw == "" ? null : Json.Parse(raw);
        }

        static void OnClientAuthenticated(BridgeClient client)
        {
            client.Send(new Dictionary<string, object>
            {
                ["type"] = "welcome",
                ["protocol"] = Protocol.Version,
                ["project"] = ProjectInfo.Root,
                ["projectName"] = ProjectInfo.Name,
                ["solution"] = ProjectInfo.SolutionPath,
                ["unityVersion"] = Application.unityVersion,
                ["state"] = State(),
                ["lastCompile"] = LastCompile(),
            });
        }

        static void OnClientsChanged()
        {
            var clients = BridgeServer.ConnectedClients;
            anyLogSubscriber = clients.Any(c => c.WantsLogs);
            if (clients.Length > 0 && NvimSettings.ResponsiveMode)
                ApplyResponsive();
            else if (clients.Length == 0)
                RestoreResponsive();
        }

        // ---------------- requests ----------------

        static void OnRequest(BridgeClient client, Dictionary<string, object> msg)
        {
            var id = msg.TryGetValue("id", out var rawId) ? rawId : null;
            void Reply(object result) => client.Send(new Dictionary<string, object>
                { ["type"] = "response", ["id"] = id, ["ok"] = true, ["result"] = result });
            void Fail(string error) => client.Send(new Dictionary<string, object>
                { ["type"] = "response", ["id"] = id, ["ok"] = false, ["error"] = error });

            switch (msg.Str("cmd"))
            {
                case "status":
                    var status = State();
                    status["lastCompile"] = LastCompile();
                    Reply(status);
                    break;
                case "refresh":
                    AssetDatabase.Refresh();
                    Reply(State());
                    break;
                case "recompile":
                    CompilationPipeline.RequestScriptCompilation();
                    Reply(State());
                    break;
                case "play":
                    // answer first: entering play mode may reload the domain and drop the socket
                    Reply(State());
                    if (!EditorApplication.isPlaying)
                        EditorApplication.isPlaying = true;
                    WakeEditor();
                    break;
                case "stop":
                    Reply(State());
                    if (EditorApplication.isPlaying)
                        EditorApplication.isPlaying = false;
                    WakeEditor();
                    break;
                case "pause":
                    if (!EditorApplication.isPlaying)
                    {
                        Fail("not in play mode");
                        break;
                    }
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    Reply(State());
                    break;
                case "subscribe":
                    client.WantsLogs = msg.Bool("logs", client.WantsLogs);
                    anyLogSubscriber = BridgeServer.ConnectedClients.Any(c => c.WantsLogs);
                    Reply(new Dictionary<string, object> { ["logs"] = client.WantsLogs });
                    break;
                default:
                    Fail("unknown command: " + msg.Str("cmd"));
                    break;
            }
        }

        /// <summary>A background editor applies play mode changes lazily; force a tick so it happens now.</summary>
        static void WakeEditor()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // ---------------- compilation ----------------

        static void OnCompilationStarted(object context)
        {
            CompileMessages.Clear();
            BridgeServer.Broadcast(BridgeServer.Event("compileStarted"));
        }

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var m in messages)
            {
                if (m.type == CompilerMessageType.Info)
                    continue;
                CompileMessages.Add(new Dictionary<string, object>
                {
                    ["file"] = m.file ?? "",
                    ["line"] = m.line,
                    ["col"] = m.column,
                    ["severity"] = m.type == CompilerMessageType.Error ? "error" : "warning",
                    ["message"] = m.message ?? "",
                    ["assembly"] = System.IO.Path.GetFileNameWithoutExtension(assemblyPath),
                });
            }
        }

        static void OnCompilationFinished(object context)
        {
            var errors = CompileMessages.Count(m => (string)m["severity"] == "error");
            var result = new Dictionary<string, object>
            {
                ["errors"] = errors,
                ["warnings"] = CompileMessages.Count - errors,
                ["messages"] = CompileMessages.ToList(),
                ["time"] = DateTime.Now.ToString("HH:mm:ss"),
            };
            // survives the domain reload that follows a successful compile; nvim reads it from "welcome"
            SessionState.SetString(LastCompileKey, Json.Serialize(result));
            var e = BridgeServer.Event("compileFinished");
            foreach (var kv in result)
                e[kv.Key] = kv.Value;
            BridgeServer.Broadcast(e);
        }

        // ---------------- play mode ----------------

        static void OnPlayModeChanged(PlayModeStateChange change) => SendPlayMode(change.ToString());

        static void OnPauseChanged(PauseState pause) => SendPlayMode(pause.ToString());

        static void SendPlayMode(string change)
        {
            var e = BridgeServer.Event("playModeChanged");
            e["change"] = change;
            e["state"] = State();
            BridgeServer.Broadcast(e);
        }

        // ---------------- console logs ----------------

        static void OnLog(string message, string stackTrace, LogType type)
        {
            // kept even with nobody connected: logs right after a domain reload (e.g. entering play mode)
            // arrive before nvim reconnects; FlushLogs keeps only the latest ones until then
            if (PendingLogs.Count > 5000)
            {
                System.Threading.Interlocked.Increment(ref droppedLogs);
                return;
            }
            PendingLogs.Enqueue(new Dictionary<string, object>
            {
                ["type"] = type.ToString(),
                ["message"] = message ?? "",
                ["stack"] = type == LogType.Error || type == LogType.Exception || type == LogType.Assert ? stackTrace ?? "" : "",
                ["time"] = DateTime.Now.ToString("HH:mm:ss"),
            });
        }

        /// <summary>Batch logs once per editor tick so a log-every-frame script can't flood the socket.</summary>
        static void FlushLogs()
        {
            if (PendingLogs.IsEmpty)
                return;
            if (!anyLogSubscriber)
            {
                while (PendingLogs.Count > Backlog && PendingLogs.TryDequeue(out _)) { }
                return;
            }
            var batch = new List<object>();
            while (batch.Count < MaxLogsPerTick && PendingLogs.TryDequeue(out var entry))
                batch.Add(entry);
            var e = BridgeServer.Event("logs");
            e["entries"] = batch;
            e["dropped"] = System.Threading.Interlocked.Exchange(ref droppedLogs, 0);
            BridgeServer.Broadcast(e, c => c.WantsLogs);
        }

        // ---------------- responsive mode ----------------

        static void ApplyResponsive()
        {
            if (SessionState.GetString(ResponsiveKey, "") != "")
                return;
            SessionState.SetString(ResponsiveKey,
                EditorPrefs.GetInt("InteractionMode", 0) + ";" + EditorPrefs.GetInt("ApplicationIdleTime", 4));
            EditorPrefs.SetInt("InteractionMode", 1);
            EditorPrefs.SetInt("ApplicationIdleTime", 0);
            UpdateInteractionMode();
        }

        static void RestoreResponsive()
        {
            var saved = SessionState.GetString(ResponsiveKey, "");
            if (saved == "")
                return;
            var parts = saved.Split(';');
            EditorPrefs.SetInt("InteractionMode", int.Parse(parts[0]));
            EditorPrefs.SetInt("ApplicationIdleTime", int.Parse(parts[1]));
            SessionState.EraseString(ResponsiveKey);
            UpdateInteractionMode();
        }

        static void UpdateInteractionMode()
        {
            // internal API (the test framework uses it the same way)
            typeof(EditorApplication)
                .GetMethod("UpdateInteractionModeSettings", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(null, null);
        }
    }
}

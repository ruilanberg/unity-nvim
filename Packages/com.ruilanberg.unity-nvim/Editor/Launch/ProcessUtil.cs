using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace UnityNvim.Editor.Launch
{
    public static class ProcessUtil
    {
        /// <summary>
        /// Quote one argument for ProcessStartInfo.Arguments. Windows follows CommandLineToArgvW; on macOS/Linux
        /// Unity's Mono splits Arguments like a POSIX shell (g_shell_parse_argv), so single quotes are used.
        /// </summary>
        public static string Quote(string arg, bool windows)
        {
            if (arg == null)
                arg = "";
            return windows ? QuoteWindows(arg) : QuotePosix(arg);
        }

        public static string QuoteWindows(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0)
                return arg;
            var sb = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string QuotePosix(string arg)
        {
            if (arg.Length > 0 && arg.All(c => char.IsLetterOrDigit(c) || "@%+=:,./-_".IndexOf(c) >= 0))
                return arg;
            return "'" + arg.Replace("'", "'\\''") + "'";
        }

        public static string JoinArgs(IEnumerable<string> args, bool windows) =>
            string.Join(" ", args.Select(a => Quote(a, windows)));

        /// <summary>Escape text to sit inside an AppleScript "double-quoted" string.</summary>
        public static string AppleScriptEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Split a user command line into tokens (double/single quotes group, backslash escapes outside quotes on POSIX).</summary>
        public static List<string> SplitCommandLine(string commandLine)
        {
            var tokens = new List<string>();
            var cur = new StringBuilder();
            var inToken = false;
            char quote = '\0';
            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    else
                        cur.Append(c);
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    inToken = true;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    if (inToken)
                    {
                        tokens.Add(cur.ToString());
                        cur.Clear();
                        inToken = false;
                    }
                    continue;
                }
                cur.Append(c);
                inToken = true;
            }
            if (inToken)
                tokens.Add(cur.ToString());
            return tokens;
        }

        /// <summary>Find an executable: absolute path that exists, or a name searched in PATH (+ PATHEXT on Windows).</summary>
        public static string Which(string nameOrPath, bool windows)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return null;
            if (nameOrPath.IndexOf('/') >= 0 || nameOrPath.IndexOf('\\') >= 0)
                return File.Exists(nameOrPath) ? nameOrPath : null;
            var exts = windows && Path.GetExtension(nameOrPath) == ""
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
                : new[] { "" };
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (dir.Length == 0)
                    continue;
                foreach (var ext in exts)
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(dir.Trim('"'), nameOrPath + ext.ToLowerInvariant());
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            return null;
        }

        public struct Result
        {
            public int ExitCode;
            public string Output;
            public bool TimedOut;
        }

        /// <summary>Run and wait (no console window), capturing stdout+stderr.</summary>
        public static Result Run(string exe, IEnumerable<string> args, int timeoutMs, bool windows)
        {
            var psi = new ProcessStartInfo(exe, JoinArgs(args, windows))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = new Process { StartInfo = psi })
            {
                var output = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { /* already gone */ }
                    return new Result { ExitCode = -1, TimedOut = true, Output = output.ToString() };
                }
                p.WaitForExit(); // flush async readers
                lock (output)
                    return new Result { ExitCode = p.ExitCode, Output = output.ToString() };
            }
        }

        /// <summary>Start without waiting (terminals, GUI apps).</summary>
        public static void Start(string exe, IEnumerable<string> args, string workingDirectory, bool windows)
        {
            var psi = new ProcessStartInfo(exe, JoinArgs(args, windows))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? "",
            };
            using (Process.Start(psi)) { }
        }
    }
}

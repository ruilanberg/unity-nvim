using System.Collections.Generic;
using System.Linq;

namespace UnityNvim.Editor.Launch
{
    /// <summary>Everything needed to start nvim for one request.</summary>
    public sealed class LaunchContext
    {
        public string Nvim;
        public string Address;
        public string File; // may be empty ("Open C# Project")
        public int Line = 1;
        public int Column = 1;
        public string Root;
        public string Title;
        /// <summary>Wrap nvim in "$SHELL -lc 'exec ...'" so it gets the user's full environment.</summary>
        public string LoginShell;

        /// <summary>nvim plus its arguments (listen address, title, cursor, file).</summary>
        public List<string> NvimArgv()
        {
            var argv = new List<string>
            {
                Nvim,
                "--listen", Address,
                "-c", "let &titlestring = '" + Title.Replace("'", "''") + "' | set title",
            };
            if (!string.IsNullOrEmpty(File))
            {
                argv.Add("+call cursor(" + Line + "," + Column + ")");
                argv.Add(File);
            }
            return argv;
        }
    }

    public static class TemplateExpander
    {
        public static List<string> Expand(IEnumerable<string> tokens, LaunchContext ctx)
        {
            var nvimArgv = ctx.NvimArgv();
            var posixCmd = "cd " + ProcessUtil.QuotePosix(ctx.Root) + " && exec " +
                           string.Join(" ", nvimArgv.Select(ProcessUtil.QuotePosix));
            if (!string.IsNullOrEmpty(ctx.LoginShell))
            {
                posixCmd = "exec " + ProcessUtil.QuotePosix(ctx.LoginShell) + " -lc " + ProcessUtil.QuotePosix(posixCmd);
                nvimArgv = new List<string> { ctx.LoginShell, "-lc", "exec " + string.Join(" ", nvimArgv.Select(ProcessUtil.QuotePosix)) };
            }

            var result = new List<string>();
            foreach (var token in tokens)
            {
                switch (token)
                {
                    case "{nvim_argv}":
                        result.AddRange(nvimArgv);
                        continue;
                    case "{nvim_args}":
                        // GUI front-ends (neovide) start nvim themselves: no login-shell wrapping
                        result.AddRange(ctx.NvimArgv().Skip(1));
                        continue;
                }
                result.Add(token
                    .Replace("{nvim_cmd_as}", ProcessUtil.AppleScriptEscape(posixCmd))
                    .Replace("{nvim}", ctx.Nvim)
                    .Replace("{addr}", ctx.Address)
                    .Replace("{file}", ctx.File ?? "")
                    .Replace("{line}", ctx.Line.ToString())
                    .Replace("{col}", ctx.Column.ToString())
                    .Replace("{root}", ctx.Root)
                    .Replace("{title}", ctx.Title));
            }
            return result;
        }
    }
}

using System.IO;

namespace UnityNvim.Editor.Launch
{
    /// <summary>Talks to an already running nvim through its --listen address.</summary>
    public static class NvimRemote
    {
        // Runs inside the target nvim. Uses the Lua plugin when installed (picks a sensible window, focus rules);
        // otherwise a plain :drop. Only single quotes inside, so it fits in a Vimscript "..." string.
        const string OpenLua =
            "(function(f, l, c) " +
            "local ok, m = pcall(require, 'unity-nvim') " +
            "if ok and m.open then return m.open(f, l, c) end " +
            "vim.cmd('stopinsert') " +
            "if f ~= '' then vim.cmd('drop ' .. vim.fn.fnameescape(f)) pcall(vim.api.nvim_win_set_cursor, 0, { l, c - 1 }) end " +
            "return true end)(_A[1], _A[2], _A[3])";

        /// <summary>Vimscript expression for --remote-expr that opens file at line/col (1-based).</summary>
        public static string OpenExpression(string file, int line, int column) =>
            "luaeval(\"" + OpenLua + "\", ['" + (file ?? "").Replace("'", "''") + "', " + line + ", " + column + "])";

        /// <summary>True when an nvim answered on that address and opened the file.</summary>
        public static bool TryOpen(string nvim, string address, string file, int line, int column, bool windows)
        {
            if (!windows && !File.Exists(address))
                return false;
            var r = ProcessUtil.Run(nvim, new[] { "--headless", "--server", address, "--remote-expr", OpenExpression(file, line, column) }, 5000, windows);
            // a dead server makes nvim print an error (and on some versions still exit 0)
            return !r.TimedOut && r.ExitCode == 0 && r.Output.Trim().EndsWith("true", System.StringComparison.Ordinal);
        }

        /// <summary>On macOS/Linux a crashed nvim leaves its socket file behind and --listen would then fail.</summary>
        public static void RemoveStaleSocket(string nvim, string address, bool windows)
        {
            if (windows || !File.Exists(address))
                return;
            var r = ProcessUtil.Run(nvim, new[] { "--headless", "--server", address, "--remote-expr", "1" }, 3000, false);
            if (r.ExitCode == 0 && r.Output.Trim() == "1")
                return;
            try { File.Delete(address); } catch (IOException) { /* in use after all */ }
        }
    }
}

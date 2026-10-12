using System;
using System.Runtime.InteropServices;
using System.Text;

namespace UnityNvim.Editor.Launch
{
    /// <summary>Brings the project's nvim window to the front (best effort; nothing on Wayland).</summary>
    public static class WindowFocus
    {
        public static void Focus(string title, HostOS os, string macApp)
        {
            try
            {
                switch (os)
                {
                    case HostOS.Windows:
                        FocusWindows(title);
                        break;
                    case HostOS.Mac:
                        if (!string.IsNullOrEmpty(macApp))
                            ProcessUtil.Start("/usr/bin/osascript", new[] { "-e", "tell application \"" + macApp + "\" to activate" }, null, false);
                        break;
                    default:
                        if (ProcessUtil.Which("wmctrl", false) != null)
                            ProcessUtil.Start("wmctrl", new[] { "-a", title }, null, false);
                        else if (ProcessUtil.Which("xdotool", false) != null)
                            ProcessUtil.Start("xdotool", new[] { "search", "--name", title, "windowactivate" }, null, false);
                        break;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[unity-nvim] could not focus the nvim window: " + e.Message);
            }
        }

        // ---- Win32 ----
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        const int SW_RESTORE = 9;

        /// <summary>Returns false when no visible window has the title (e.g. not created yet).</summary>
        public static bool FocusWindows(string title)
        {
            var found = IntPtr.Zero;
            var sb = new StringBuilder(512);
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h))
                    return true;
                sb.Clear();
                GetWindowText(h, sb, sb.Capacity);
                if (sb.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0)
                    return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero)
                return false;
            if (IsIconic(found))
                ShowWindow(found, SW_RESTORE);
            return SetForegroundWindow(found);
        }
    }
}

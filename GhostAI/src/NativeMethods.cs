using System;
using System.Runtime.InteropServices;

namespace GhostAI {
    public static class NativeMethods {
        // Core Display Affinity Flags
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+ (Build 19041+)

        // Window Styles
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        // SetWindowPos Flags
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_FRAMECHANGED = 0x0020;

        // Hotkey Modifiers
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // Window Messages
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HT_CAPTION = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        /// <summary>
        /// Applies full stealth to the window:
        /// 1. Excludes from screen capture/sharing via SetWindowDisplayAffinity
        /// 2. Hides from Alt-Tab via WS_EX_TOOLWINDOW
        /// 3. Removes from Taskbar via removing WS_EX_APPWINDOW
        /// </summary>
        public static bool ApplyStealthMode(IntPtr hWnd) {
            bool affinitySuccess = false;
            try {
                // Try WDA_EXCLUDEFROMCAPTURE first (Windows 10 2004+)
                affinitySuccess = SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE);
                if (!affinitySuccess) {
                    // Fallback to WDA_MONITOR (shows black box in older Windows)
                    affinitySuccess = SetWindowDisplayAffinity(hWnd, WDA_MONITOR);
                }
            } catch { }

            try {
                // Apply tool window style to hide from Alt-Tab
                IntPtr exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
                long styleLong = exStyle.ToInt64();
                styleLong = (styleLong | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(styleLong));

                // Refresh window frame
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            } catch { }

            return affinitySuccess;
        }
    }
}

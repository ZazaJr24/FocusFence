using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusFence
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RectI : IEquatable<RectI>
    {
        public int Left, Top, Right, Bottom;
        public RectI(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public bool Equals(RectI b) { return Left == b.Left && Top == b.Top && Right == b.Right && Bottom == b.Bottom; }
        public override bool Equals(object o) { return o is RectI && Equals((RectI)o); }
        public override int GetHashCode() { return Left ^ Top ^ Right ^ Bottom; }
        public override string ToString() { return Width + " × " + Height + " px"; }
        public static RectI Intersect(RectI a, RectI b) { return new RectI(Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top), Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom)); }
    }

    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public int ProcessId;
        public string ProcessName = "";
        public string Title = "";
        public string WindowClass = "";
        public RectI Bounds, MonitorBounds;
        public long ExtendedStyle, Style;
        public IntPtr Owner;
        public bool Visible, Minimized, Cloaked;
        public override string ToString() { return Title + "  ·  " + ProcessName; }
    }

    internal static class Native
    {
        internal const long Transparent = 0x20, Layered = 0x80000;
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        internal delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out RectI r);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point p);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RectI r);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool ClipCursor(ref RectI rect);
        [DllImport("user32.dll", EntryPoint = "ClipCursor", SetLastError = true)] internal static extern bool ReleaseCursor(IntPtr empty);
        [DllImport("user32.dll")] internal static extern bool GetClipCursor(out RectI rect);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] static extern int GetLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] static extern IntPtr GetLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] static extern int SetLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint color, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        internal static void ApplyWindowAppearance(IntPtr hwnd)
        {
            int dark = 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int roundCorners = 2;
                DwmSetWindowAttribute(hwnd, 33, ref roundCorners, sizeof(int));
            }
        }

        internal static long GetStyle(IntPtr hwnd) { return IntPtr.Size == 8 ? GetLong64(hwnd, -20).ToInt64() : (uint)GetLong32(hwnd, -20); }
        internal static bool SetStyle(IntPtr hwnd, long style)
        {
            SetLastError(0);
            long previous = IntPtr.Size == 8 ? SetLong64(hwnd, -20, new IntPtr(style)).ToInt64() : SetLong32(hwnd, -20, (int)style);
            return previous != 0 || Marshal.GetLastWin32Error() == 0;
        }
        internal static int Pid(IntPtr hwnd) { uint p; GetWindowThreadProcessId(hwnd, out p); return (int)p; }
        internal static WindowInfo Describe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
            try
            {
                using (var p = Process.GetProcessById(Pid(hwnd)))
                {
                var text = new StringBuilder(1024); GetWindowText(hwnd, text, text.Capacity);
                var className = new StringBuilder(256); GetClassName(hwnd, className, className.Capacity);
                RectI bounds; GetWindowRect(hwnd, out bounds);
                var screen = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                int cloaked; DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int));
                return new WindowInfo { Handle = hwnd, ProcessId = p.Id, ProcessName = p.ProcessName, Title = text.ToString(),
                    WindowClass = className.ToString(), Bounds = bounds, MonitorBounds = new RectI(screen.Left, screen.Top, screen.Right, screen.Bottom),
                    ExtendedStyle = GetStyle(hwnd), Style = IntPtr.Size == 8 ? GetLong64(hwnd, -16).ToInt64() : (uint)GetLong32(hwnd, -16),
                    Owner = GetWindow(hwnd, 4), Visible = IsWindowVisible(hwnd), Minimized = IsIconic(hwnd), Cloaked = cloaked != 0 };
                }
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
        internal static List<WindowInfo> Windows()
        {
            var result = new List<WindowInfo>();
            int own = Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr d)
            {
                if (IsWindowVisible(h) && Pid(h) != own)
                {
                    var w = Describe(h);
                    if (w != null && !w.Cloaked && (!string.IsNullOrWhiteSpace(w.Title) || (w.ExtendedStyle & 8) != 0)) result.Add(w);
                }
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(WindowInfo a, WindowInfo b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); });
            return result;
        }
        internal static bool ClientBounds(IntPtr hwnd, out RectI rect)
        {
            rect = new RectI(); RectI local;
            if (!IsWindow(hwnd) || IsIconic(hwnd) || !GetClientRect(hwnd, out local)) return false;
            var origin = new Point(); if (!ClientToScreen(hwnd, ref origin)) return false;
            rect = new RectI(origin.X, origin.Y, origin.X + local.Width, origin.Y + local.Height);
            return rect.Width > 1 && rect.Height > 1;
        }
    }
}

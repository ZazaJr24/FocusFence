using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace FocusFence
{
    internal interface ICursorClip
    {
        bool Set(ref RectI bounds);
        bool Read(out RectI bounds);
        bool Release();
    }
    internal sealed class WindowsCursorClip : ICursorClip
    {
        public bool Set(ref RectI bounds) { return Native.ClipCursor(ref bounds); }
        public bool Read(out RectI bounds) { return Native.GetClipCursor(out bounds); }
        public bool Release() { return Native.ReleaseCursor(IntPtr.Zero); }
    }
    // The decision function is independent of Win32; the same policy is exercised by the tests.
    internal static class GuardPolicy
    {
        internal static bool ShouldLock(bool armed, DateTime now, DateTime pauseUntil, GameProfile profile, WindowInfo foreground, int ownPid)
        {
            return armed && now >= pauseUntil && profile != null && foreground != null && foreground.ProcessId != ownPid
                && string.Equals(profile.ProcessName, foreground.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        internal static bool Bounds(RectI client, RectI monitor, GameProfile profile, out RectI bounds)
        {
            bounds = profile.Area == "monitor" ? monitor : RectI.Intersect(client, monitor);
            int inset = Math.Max(0, Math.Min(50, profile.Inset));
            bounds = new RectI(bounds.Left + inset, bounds.Top + inset, bounds.Right - inset, bounds.Bottom - inset);
            return bounds.Width >= 4 && bounds.Height >= 4;
        }
    }
    public class RecoveryState
    {
        public bool Clipped;
        public RectI Bounds;
        public long PipHandle;
        public int PipPid;
        public long PipStartTicks;
        public long AddedStyles;
    }
    internal sealed class GuardEngine : IDisposable
    {
        internal bool Armed, Locked;
        internal DateTime PauseUntil = DateTime.MinValue;
        internal GameProfile Profile;
        internal WindowInfo Pip;
        internal string Status = "Protection off · Mouse released";
        internal string Error = "";
        internal RectI CurrentBounds;
        internal double SessionSeconds;
        internal event Action Changed;
        private RecoveryState recovery = new RecoveryState();
        private readonly string recoveryPath;
        private DateTime lastTick = DateTime.UtcNow;
        private readonly int ownPid = Process.GetCurrentProcess().Id;
        private readonly ICursorClip cursor;
        private readonly Func<IntPtr> foreground;
        private IntPtr eventHook;
        private Native.WinEventProc eventProc;
        private IntPtr lastForeground;
        private WindowInfo lastDescription;
        private bool disposed;
        internal GuardEngine(bool watch, Func<IntPtr> foregroundOverride = null, ICursorClip cursorOverride = null)
        {
            cursor = cursorOverride ?? new WindowsCursorClip();
            foreground = foregroundOverride ?? Native.GetForegroundWindow;
            recoveryPath = Path.Combine(Storage.Folder, "recovery-" + ownPid + ".xml");
            if (watch)
            {
                // One small companion waits for process exit and restores only the resources we own.
                var info = new ProcessStartInfo(App.ExecutablePath, "--watch " + ownPid + " \"" + recoveryPath + "\"");
                info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
                try { Process.Start(info); }
                catch (Exception e) { Error = "Recovery companion could not start: " + e.Message; Storage.Log(Error); }
                eventProc = delegate { Tick(); };
                eventHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, eventProc, 0, 0, 0);
            }
        }
        internal void Toggle()
        {
            Armed = !Armed; PauseUntil = DateTime.MinValue;
            if (Armed) { SessionSeconds = 0; Error = ""; }
            Tick(); Signal();
            Storage.Log(Armed ? "Protection enabled" : "Protection disabled");
        }
        internal void Stop()
        {
            Armed = false; PauseUntil = DateTime.MinValue; Release(); Status = Locked || recovery.PipHandle != 0 ? "Release incomplete · " + Error : "Protection off · Mouse released"; Signal();
        }
        internal void Pause()
        {
            if (!Armed) return;
            PauseUntil = PauseUntil > DateTime.UtcNow ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(30);
            Tick(); Signal();
        }
        internal void ChangeProfile(GameProfile profile)
        {
            if (ReferenceEquals(Profile, profile)) return;
            Release(); Profile = profile; lastDescription = null; Tick();
        }
        internal void Tick()
        {
            if (disposed) return;
            var now = DateTime.UtcNow;
            if (Locked) SessionSeconds += Math.Max(0, Math.Min(1, (now - lastTick).TotalSeconds));
            lastTick = now;
            try
            {
                IntPtr fg = foreground();
                if (fg != lastForeground || lastDescription == null || Native.Pid(fg) != lastDescription.ProcessId)
                { lastForeground = fg; lastDescription = Native.Describe(fg); }
                if ((Pip != null && fg == Pip.Handle) || !GuardPolicy.ShouldLock(Armed, now, PauseUntil, Profile, lastDescription, ownPid))
                {
                    Release();
                    if (Locked || recovery.PipHandle != 0) { Status = "Release incomplete · " + Error; return; }
                    Status = !Armed ? "Protection off · Mouse released" : now < PauseUntil ? "Pause · " + Math.Ceiling((PauseUntil - now).TotalSeconds) + " s" : Profile == null ? "Waiting for a game" : "Waiting for " + Profile.Name;
                    return;
                }
                RectI client, bounds;
                if (!Native.ClientBounds(fg, out client)) { Release(); Status = "Game window is not available"; return; }
                Screen monitor = Screen.FromHandle(fg);
                if (Profile.Area == "monitor" && !string.IsNullOrEmpty(Profile.Monitor))
                {
                    bool found = false;
                    foreach (var screen in Screen.AllScreens) if (screen.DeviceName == Profile.Monitor) { monitor = screen; found = true; break; }
                    if (!found) { Release(); Status = "The selected monitor is disconnected"; return; }
                }
                var m = monitor.Bounds;
                if (!GuardPolicy.Bounds(client, new RectI(m.Left, m.Top, m.Right, m.Bottom), Profile, out bounds))
                { Release(); Status = "Confinement area is too small or outside the monitor"; return; }
                if (!Locked || !bounds.Equals(CurrentBounds))
                {
                    // Persist before the mutation, so a crash cannot leave an unrecorded clip behind.
                    recovery.Clipped = true; recovery.Bounds = bounds; Persist();
                }
                if (!cursor.Set(ref bounds)) throw new InvalidOperationException("Windows denied cursor confinement.");
                CurrentBounds = bounds; Locked = true;
                if (Profile.PipClickThrough && Pip != null && Pip.Handle != fg) ApplyPip(); else RestorePip();
                Status = "Protected · " + Profile.Name;
            }
            catch (Exception e)
            {
                Error = e.Message; Armed = false; Release(); Status = "Protection stopped · " + Error; Storage.Log(Status); Signal();
            }
        }
        private void ApplyPip()
        {
            if (!Native.IsWindow(Pip.Handle) || Native.Pid(Pip.Handle) != Pip.ProcessId) { RestorePip(); Pip = null; return; }
            if (recovery.PipHandle == Pip.Handle.ToInt64() && recovery.PipPid == Pip.ProcessId) return;
            RestorePip();
            long style = Native.GetStyle(Pip.Handle);
            long added = (Native.Transparent | Native.Layered) & ~style;
            recovery.PipHandle = Pip.Handle.ToInt64(); recovery.PipPid = Pip.ProcessId;
            recovery.PipStartTicks = Process.GetProcessById(Pip.ProcessId).StartTime.ToUniversalTime().Ticks;
            recovery.AddedStyles = added; Persist();
            if (!Native.SetStyle(Pip.Handle, style | Native.Transparent | Native.Layered))
                throw new InvalidOperationException("This PiP window denied click-through. Check whether it is running as administrator.");
            if ((added & Native.Layered) != 0 && !Native.SetLayeredWindowAttributes(Pip.Handle, 0, 255, 2))
                throw new InvalidOperationException("Could not enable PiP click-through.");
        }
        private void RestorePip()
        {
            if (recovery.PipHandle == 0) return;
            if (!RestoreStyles(recovery)) throw new InvalidOperationException("Could not release the PiP window. Please close the video window.");
            recovery.PipHandle = 0; recovery.AddedStyles = 0; Persist();
        }
        internal static bool RestoreStyles(RecoveryState r)
        {
            IntPtr hwnd = new IntPtr(r.PipHandle);
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || Native.Pid(hwnd) != r.PipPid) return true;
            try { if (Process.GetProcessById(r.PipPid).StartTime.ToUniversalTime().Ticks != r.PipStartTicks) return true; }
            catch (ArgumentException) { return true; }
            return Native.SetStyle(hwnd, Native.GetStyle(hwnd) & ~r.AddedStyles);
        }
        private void Release()
        {
            if (!recovery.Clipped && recovery.PipHandle == 0) { Locked = false; return; }
            if (recovery.Clipped)
            {
                RectI actual;
                // Do not overwrite another app's more recent cursor clip.
                bool read = cursor.Read(out actual);
                bool released = read && (!actual.Equals(recovery.Bounds) || cursor.Release());
                if (!released) Error = "Windows could not release the cursor.";
                else { recovery.Clipped = false; Locked = false; }
            }
            try { RestorePip(); }
            catch (Exception e) { Error = e.Message; Storage.Log(Error); }
            try { Persist(); }
            catch (Exception e) { Error = "Could not update the recovery file: " + e.Message; Storage.Log(Error); }
        }
        private void Persist()
        {
            if (!recovery.Clipped && recovery.PipHandle == 0)
            { if (File.Exists(recoveryPath)) File.Delete(recoveryPath); return; }
            Storage.Write(recoveryPath, recovery);
        }
        private void Signal() { if (Changed != null) Changed(); }
        internal static int Watch(int parentId, string path)
        {
            try { Process.GetProcessById(parentId).WaitForExit(); }
            catch (ArgumentException) { }
            try
            {
                if (!File.Exists(path)) return 0;
                var state = Storage.Read<RecoveryState>(path);
                RectI actual;
                bool cursorRestored = !state.Clipped;
                if (state.Clipped && Native.GetClipCursor(out actual)) cursorRestored = !actual.Equals(state.Bounds) || Native.ReleaseCursor(IntPtr.Zero);
                bool restored = RestoreStyles(state) && cursorRestored;
                if (restored) File.Delete(path);
                Storage.Log("Recovery after process exit: " + (restored ? "successful" : "close the PiP window"));
                return restored ? 0 : 1;
            }
            catch (Exception e) { Storage.Log("Recovery: " + e.Message); return 1; }
        }
        public void Dispose()
        {
            if (disposed) return;
            Armed = false; Release(); disposed = true;
            if (eventHook != IntPtr.Zero) Native.UnhookWinEvent(eventHook);
            GC.KeepAlive(eventProc);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace FocusFence
{
    internal static class SelfTests
    {
        private static readonly List<string> results = new List<string>();
        private static void Assert(bool pass, string name) { if (!pass) throw new Exception(name); results.Add("PASS  " + name); }
        // Foreground and confinement are substituted; window geometry and styles use real Win32 windows.
        private sealed class TestCursor : ICursorClip
        {
            internal RectI Current = new RectI(-3000, -2000, 6000, 4000);
            internal bool RejectSet;
            public bool Set(ref RectI r) { if (RejectSet) return false; Current = r; return true; }
            public bool Read(out RectI r) { r = Current; return true; }
            public bool Release() { Current = new RectI(-3000, -2000, 6000, 4000); return true; }
        }
        internal static int TestHost(string path)
        {
            var game = new Forms.Form { Text = "FocusFence Test Game", StartPosition = Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(80, 80), Size = new System.Drawing.Size(640, 420) };
            var video = new Forms.Form { Text = "FocusFence Test PiP", StartPosition = Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(200, 180), Size = new System.Drawing.Size(180, 120) };
            File.WriteAllLines(path, new[] { game.Handle.ToInt64().ToString(), video.Handle.ToInt64().ToString() });
            Forms.Application.Run(); game.Dispose(); video.Dispose(); return 0;
        }
        internal static int CrashWorker(string handles, string ready)
        {
            var lines = File.ReadAllLines(handles);
            var game = Native.Describe(new IntPtr(long.Parse(lines[0]))); var pip = Native.Describe(new IntPtr(long.Parse(lines[1])));
            var engine = new GuardEngine(true, delegate { return game.Handle; }, new TestCursor()) { Profile = new GameProfile { ProcessName = game.ProcessName }, Pip = pip, Armed = true };
            engine.Tick(); File.WriteAllText(ready, engine.Locked ? "locked" : "failed");
            Thread.Sleep(20000); engine.Dispose(); return 0;
        }
        private static bool Wait(Func<bool> condition, int milliseconds)
        {
            var time = Stopwatch.StartNew();
            while (time.ElapsedMilliseconds < milliseconds) { if (condition()) return true; Thread.Sleep(30); }
            return condition();
        }
        internal static int Inspect(string output)
        {
            var windows = Native.Windows();
            var rows = windows.Where(delegate(WindowInfo w) { return WindowDiscovery.IsBrowser(w.ProcessName); }).Select(delegate(WindowInfo w)
            { return w.ProcessName + " | " + w.Title + " | " + w.WindowClass + " | ex=" + w.ExtendedStyle.ToString("X") + " | style=" + w.Style.ToString("X") + " | " + w.Bounds + " | score=" + WindowDiscovery.PipScore(w); }).ToList();
            rows.Add("PiP candidates: " + WindowDiscovery.PipCandidates(windows).Count);
            File.WriteAllLines(output, rows.ToArray()); return 0;
        }
        private static WindowInfo PipFixture(long handle, string title, string process)
        {
            return new WindowInfo { Handle = new IntPtr(handle), ProcessId = 1234, Title = title, ProcessName = process,
                WindowClass = process == "firefox" ? "MozillaDialogClass" : "Chrome_WidgetWin_1", ExtendedStyle = 8,
                Bounds = new RectI(800, 500, 1200, 750), MonitorBounds = new RectI(0, 0, 1920, 1080), Visible = true };
        }
        private static void DiscoveryTests()
        {
            var chrome = PipFixture(1001, "Picture in picture", "chrome");
            var firefox = PipFixture(1002, "Picture-in-Picture", "firefox");
            Assert(WindowDiscovery.PipScore(chrome) == 100 && WindowDiscovery.PipScore(firefox) == 100, "Auto PiP recognizes Chrome and Firefox native signatures");
            foreach (string title in new[] { "Bild-im-Bild", "Bild in Bild", "Incrustation vidéo", "Imagen en imagen", "Immagine nell’immagine" })
                Assert(WindowDiscovery.PipScore(PipFixture(1003, title, "msedge")) == 100, "Auto PiP recognizes localized title: " + title);
            var tab = PipFixture(1004, "Picture-in-Picture – Google Chrome", "chrome"); tab.Style = 0x30000;
            Assert(WindowDiscovery.PipScore(tab) == 0, "Regular browser tab mentioning PiP is never a match");
            var fakeApp = PipFixture(1005, "Picture in picture", "notepad");
            Assert(WindowDiscovery.PipScore(fakeApp) == 0, "Non-browser window cannot impersonate PiP by title");
            var prompt = PipFixture(1006, "Picture in picture", "msedge"); prompt.WindowClass = "#32770";
            Assert(WindowDiscovery.PipScore(prompt) == 0, "Browser dialogs are excluded by native class");
            chrome.Minimized = true; Assert(WindowDiscovery.PipScore(chrome) == 0, "Minimized PiP is ignored"); chrome.Minimized = false;
            chrome.Cloaked = true; Assert(WindowDiscovery.PipScore(chrome) == 0, "Windows on other virtual desktops are ignored"); chrome.Cloaked = false;
            chrome.ExtendedStyle = 0; Assert(WindowDiscovery.PipScore(chrome) == 0, "Normal non-topmost browser window is ignored"); chrome.ExtendedStyle = 8;
            var docPip = PipFixture(1007, "Video player", "brave"); docPip.ExtendedStyle |= 0x80;
            Assert(WindowDiscovery.PipScore(docPip) > 0, "Compact topmost browser tool window supports custom Document PiP title");
            docPip.Style = 0x30000; Assert(WindowDiscovery.PipScore(docPip) == 0, "Generic resizable topmost browser window is excluded");
            var both = WindowDiscovery.PipCandidates(new[] { chrome, firefox, tab, fakeApp });
            Assert(WindowDiscovery.ChoosePip(both, null) == null, "Ambiguous PiP windows require selection instead of guessing");
            Assert(WindowDiscovery.ChoosePip(both, chrome) == chrome, "Multiple PiP windows retain the current selection");
            var restarted = PipFixture(2001, "Bild-in-Bild", "msedge"); restarted.ProcessId = 9876;
            Assert(WindowDiscovery.ChoosePip(new List<WindowInfo> { restarted }, chrome) == restarted, "Browser restart automatically replaces stale window and process IDs");
            Assert(WindowDiscovery.ChoosePip(new List<WindowInfo>(), chrome) == null, "Closed PiP clears automatic selection");
            var game = new WindowInfo { Handle = new IntPtr(3001), ProcessName = "FortniteClient-Win64-Shipping", Title = "Fortnite", Visible = true };
            var browser = PipFixture(3002, "Fortnite - Browser", "chrome");
            Assert(WindowDiscovery.GameName(game) == "Fortnite" && WindowDiscovery.GameName(browser) == null, "Known games detected by process, not browser title");
            Assert(WindowDiscovery.ChooseGame(new[] { game, browser }, browser.Handle, null, new List<GameProfile>()) == game, "Single known running game is offered without profile setup");
            var custom = new WindowInfo { Handle = new IntPtr(3003), ProcessName = "custom", Visible = true };
            var profiles = new List<GameProfile> { new GameProfile { ProcessName = "custom" } };
            Assert(WindowDiscovery.ChooseGame(new[] { game, custom }, custom.Handle, null, profiles) == custom, "Saved games follow foreground focus");
        }
        internal static int Smoke(string output)
        {
            int code = 1;
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var window = App.LoadWindow(); window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -20000;
                using (var controller = new MainController(window, false))
                {
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        code = controller.ReadyForSmokeTest ? 0 : 1;
                        File.WriteAllText(output, code == 0 ? "PASS  Normal application starts with background discovery, tray, emergency hotkey, event loop and protection disabled." : "FAIL  Normal startup incomplete.");
                        controller.CloseForReal();
                    };
                    timer.Start(); application.Run();
                }
            }
            catch (Exception e) { File.WriteAllText(output, "FAIL  " + e); }
            return code;
        }
        private static void CoreTests(string scratch)
        {
                DiscoveryTests();
                var p = new GameProfile { ProcessName = "test", Inset = 2 };
                var w = new WindowInfo { ProcessId = 123, ProcessName = "TEST" }; var now = DateTime.UtcNow;
                Assert(GuardPolicy.ShouldLock(true, now, now, p, w, 321), "Selected foreground process locks (case insensitive)");
                Assert(!GuardPolicy.ShouldLock(false, now, now, p, w, 321), "Disarmed never locks");
                Assert(!GuardPolicy.ShouldLock(true, now, now.AddSeconds(1), p, w, 321), "Pause suppresses locking");
                Assert(!GuardPolicy.ShouldLock(true, now, now, p, w, 123), "Own app never locks");
                Assert(!GuardPolicy.ShouldLock(true, now, now, null, w, 321), "Missing profile never locks");
                w.ProcessName = "other";
                Assert(!GuardPolicy.ShouldLock(true, now, now, p, w, 321), "Alt-Tab to another process releases");
                RectI bounds;
                Assert(GuardPolicy.Bounds(new RectI(-500, 30, 800, 700), new RectI(0, 0, 1920, 1080), p, out bounds) && bounds.Equals(new RectI(2, 32, 798, 698)), "Client clipped to monitor with inset");
                Assert(GuardPolicy.Bounds(new RectI(-1800, 30, -500, 700), new RectI(-1920, 0, 0, 1080), p, out bounds) && bounds.Left == -1798, "Negative multi-monitor coordinates preserved");
                p.Area = "monitor";
                Assert(GuardPolicy.Bounds(new RectI(30, 30, 800, 700), new RectI(0, 0, 1920, 1080), p, out bounds) && bounds.Equals(new RectI(2, 2, 1918, 1078)), "Monitor mode uses full monitor");
                p.Area = "window"; p.Inset = 50;
                Assert(!GuardPolicy.Bounds(new RectI(0, 0, 50, 50), new RectI(0, 0, 1920, 1080), p, out bounds), "Invalid tiny region rejected");
                string xml = Path.Combine(scratch, "settings.xml");
                var saved = new Settings(); saved.Profiles.Add(new GameProfile { Name = "Prüfung & <Spiel>", ProcessName = "sample", Inset = 7 });
                Storage.Write(xml, saved); var loaded = Storage.Read<Settings>(xml);
                Assert(loaded.Profiles.Count == 1 && loaded.Profiles[0].Name == "Prüfung & <Spiel>" && loaded.Profiles[0].Inset == 7, "Settings XML round trip preserves user data");
                saved.Sessions = 4; Storage.Write(xml, saved);
                Assert(Storage.Read<Settings>(xml).Sessions == 4, "Atomic replacement updates existing settings");
        }
        internal static int RunCore(string output)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "FocusFence-core-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            int code = 0;
            try { CoreTests(scratch); }
            catch (Exception e) { results.Add("FAIL  " + e); code = 1; }
            File.WriteAllLines(output, results.ToArray());
            return code;
        }
        internal static int Run(string output)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "FocusFence-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
            int code = 0; Process host = null, crash = null;
            try
            {
                CoreTests(scratch);
                string handles = Path.Combine(scratch, "handles.txt");
                host = Process.Start(new ProcessStartInfo(App.ExecutablePath, "--test-host \"" + handles + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                Assert(Wait(delegate { return File.Exists(handles) && File.ReadAllLines(handles).Length == 2; }, 6000), "Hidden native game and PiP test windows created");
                var ids = File.ReadAllLines(handles); var game = Native.Describe(new IntPtr(long.Parse(ids[0]))); var pip = Native.Describe(new IntPtr(long.Parse(ids[1])));
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var view = App.LoadWindow();
                using (var controller = new MainController(view, true))
                {
                    view.Width = view.MinWidth; view.Height = view.MinHeight;
                    view.ShowActivated = false; view.ShowInTaskbar = false; view.Left = -20000; view.Show(); view.UpdateLayout();
                    Assert(Math.Abs(view.ActualWidth - view.MinWidth) < 1 && Math.Abs(view.ActualHeight - view.MinHeight) < 1, "WPF view loads and lays out at minimum dimensions");
                    foreach (var page in new[] { "Profiles", "Settings", "Help", "Overview" })
                    {
                        ((Button)view.FindName("Nav" + page)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(((StackPanel)view.FindName(page + "Page")).Visibility == Visibility.Visible, "Navigation opens " + page);
                    }
                    Assert(!((Button)view.FindName("PauseButton")).IsEnabled, "Pause unavailable while disarmed");
                    view.UpdateLayout();
                    var pageScroll = (ScrollViewer)view.FindName("PageScroll");
                    Assert(pageScroll.ScrollableHeight < 1, "Overview fits minimum window size without scrolling (overflow: " + pageScroll.ScrollableHeight + ")");
                    var firstVideo = PipFixture(9001, "Picture in picture", "chrome");
                    controller.ApplyDiscovery(new List<WindowInfo> { firstVideo });
                    Assert(((TextBlock)view.FindName("PipWindowTitle")).Text.Contains("auto-detected"), "Controller automatically selects detected PiP without user interaction");
                    controller.ApplyDiscovery(new List<WindowInfo>());
                    Assert(((TextBlock)view.FindName("PipWindowTitle")).Text == "Waiting for a PiP window", "Controller returns to waiting state after PiP closes");
                    controller.ApplyDiscovery(new List<WindowInfo> { PipFixture(9002, "Bild-im-Bild", "msedge") });
                    Assert(((TextBlock)view.FindName("PipWindowTitle")).Text.StartsWith("Edge"), "Controller reacquires PiP after browser restart");
                    var manualVideo = PipFixture(9003, "Picture-in-Picture", "firefox");
                    var manualPicker = (ComboBox)view.FindName("PipWindows"); manualPicker.ItemsSource = new[] { manualVideo }; manualPicker.SelectedIndex = 0;
                    controller.ApplyDiscovery(new List<WindowInfo> { firstVideo, manualVideo });
                    Assert(((TextBlock)view.FindName("PipWindowTitle")).Text.Contains("selected manually"), "Manual PiP selection is not overwritten by background detection");
                    ((Button)view.FindName("AutomaticPipButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(((TextBlock)view.FindName("PipWindowTitle")).Text.Contains("auto-detected"), "Automatic discovery can be restored after manual selection");
                    controller.ApplyDiscovery(new List<WindowInfo>()); controller.ApplyDiscovery(new List<WindowInfo> { firstVideo, manualVideo });
                    Assert(((TextBlock)view.FindName("PipWindowTitle")).Text.Contains("2 video windows"), "Controller explains multiple-video ambiguity instead of silently choosing");
                    controller.ApplyDiscovery(new List<WindowInfo>());
                    var unlistedGame = new WindowInfo { Handle = new IntPtr(9010), ProcessId = 9977, ProcessName = "unlisted-game", Title = "Mein neues Spiel", Visible = true, Bounds = new RectI(0, 0, 640, 480) };
                    controller.ApplyDiscovery(new List<WindowInfo> { unlistedGame });
                    var quickPicker = (ComboBox)view.FindName("DashboardProfiles");
                    quickPicker.SelectedItem = quickPicker.Items.Cast<GameProfile>().First(delegate(GameProfile profile) { return profile.ProcessName == "unlisted-game"; });
                    Assert(((GameProfile)((ComboBox)view.FindName("EditProfiles")).SelectedItem).ProcessName == "unlisted-game", "Home game picker creates and selects a profile in one step");
                    ((Button)view.FindName("DeleteProfileButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((Button)view.FindName("ToggleGuardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert((string)((Button)view.FindName("ToggleGuardButton")).Content == "Disable protection", "One-click protection can wait for automatic game detection without prior setup");
                    ((Button)view.FindName("ToggleGuardButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((Button)view.FindName("NavProfiles")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var games = (ComboBox)view.FindName("GameWindows"); games.ItemsSource = new[] { game }; games.SelectedIndex = 0;
                    ((Button)view.FindName("AddGameButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(((StackPanel)view.FindName("ProfileEditor")).Visibility == Visibility.Visible, "Adding native game reveals profile editor");
                    ((TextBox)view.FindName("ProfileName")).Text = "Mein Testspiel";
                    ((Slider)view.FindName("InsetSlider")).Value = 8;
                    ((ComboBox)view.FindName("AreaChoice")).SelectedIndex = 1;
                    ((Button)view.FindName("SaveProfileButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var edited = (GameProfile)((ComboBox)view.FindName("DashboardProfiles")).SelectedItem;
                    Assert(edited.Name == "Mein Testspiel" && edited.Inset == 8 && edited.Area == "monitor", "Profile editor saves name, inset and area into selected profile");
                    ((Button)view.FindName("DeleteProfileButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(((TextBlock)view.FindName("NoProfilesText")).Visibility == Visibility.Visible, "Deleting last profile restores empty state");
                    controller.CloseForReal();
                }
                application.Shutdown();
                long originalStyle = Native.GetStyle(pip.Handle);
                var cursor = new TestCursor(); IntPtr foreground = game.Handle;
                using (var engine = new GuardEngine(false, delegate { return foreground; }, cursor))
                {
                    engine.Profile = new GameProfile { ProcessName = game.ProcessName, Name = "Test Game", Inset = 3 }; engine.Pip = pip;
                    engine.Toggle();
                    Assert(engine.Locked && cursor.Current.Equals(engine.CurrentBounds), "Engine confines to real game client bounds via cursor backend");
                    Assert((Native.GetStyle(pip.Handle) & (Native.Transparent | Native.Layered)) == (Native.Transparent | Native.Layered), "Real PiP receives layered click-through styles");
                    engine.Pause();
                    Assert(!engine.Locked && !cursor.Current.Equals(engine.CurrentBounds), "30-second pause releases cursor backend");
                    Assert(Native.GetStyle(pip.Handle) == originalStyle, "Pause restores exact original PiP styles");
                    engine.Pause(); Assert(engine.Locked, "Resume reapplies clip");
                    foreground = pip.Handle; engine.Tick();
                    Assert(!engine.Locked, "Selecting PiP frees it even when game and video share a process");
                    foreground = game.Handle; engine.Tick(); Assert(engine.Locked, "Returning to game restores protection");
                    engine.Stop();
                    Assert(!engine.Locked && !engine.Armed && Native.GetStyle(pip.Handle) == originalStyle, "Emergency stop restores cursor backend and real PiP");
                    engine.Toggle(); engine.Pause(); engine.PauseUntil = DateTime.UtcNow.AddMilliseconds(-1); engine.Tick();
                    Assert(engine.Locked, "Expired pause automatically resumes protection");
                    cursor.Current = new RectI(50, 50, 100, 100); engine.Stop();
                    Assert(cursor.Current.Equals(new RectI(50, 50, 100, 100)), "Another application's newer cursor clip is preserved");
                    cursor.RejectSet = true; engine.Toggle();
                    Assert(!engine.Armed && !engine.Locked && !string.IsNullOrEmpty(engine.Error), "Denied cursor operation stops protection and reports error");
                }
                Assert(Native.GetStyle(pip.Handle) == originalStyle, "Disposal restores real PiP styles");
                long extraStyle = 0x08000000; // WS_EX_NOACTIVATE, not owned by FocusFence.
                Native.SetStyle(pip.Handle, originalStyle | Native.Layered | extraStyle);
                Native.SetLayeredWindowAttributes(pip.Handle, 0, 180, 2);
                using (var preserve = new GuardEngine(false, delegate { return game.Handle; }, new TestCursor()))
                {
                    preserve.Profile = new GameProfile { ProcessName = game.ProcessName }; preserve.Pip = pip; preserve.Toggle(); preserve.Stop();
                    Assert(Native.GetStyle(pip.Handle) == (originalStyle | Native.Layered | extraStyle), "Pre-existing layered and unrelated PiP styles survive restore");
                }
                Native.SetStyle(pip.Handle, originalStyle);
                // Exercise actual Windows API without changing pointer freedom or position.
                var nativeCursor = new WindowsCursorClip(); RectI existing;
                Assert(nativeCursor.Read(out existing) && nativeCursor.Set(ref existing), "Win32 cursor API accepts unchanged existing confinement");
                string ready = Path.Combine(scratch, "ready.txt");
                crash = Process.Start(new ProcessStartInfo(App.ExecutablePath, "--crash-worker \"" + handles + "\" \"" + ready + "\"") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                Assert(Wait(delegate { return File.Exists(ready) && new FileInfo(ready).Length > 0; }, 6000) && File.ReadAllText(ready) == "locked", "Crash worker establishes protected test state");
                crash.Kill(); crash.WaitForExit(3000);
                Assert(Wait(delegate { return Native.GetStyle(pip.Handle) == originalStyle; }, 5000), "Watchdog restores real PiP after forced process termination");
                using (var hotkeyWindow = new HwndSource(new HwndSourceParameters("FocusFence Hotkey Test") { ParentWindow = new IntPtr(-3) }))
                {
                    bool registered = Native.RegisterHotKey(hotkeyWindow.Handle, 77, 0x4003, 0x7B);
                    Assert(registered, "Emergency global hotkey registers");
                    if (registered) Native.UnregisterHotKey(hotkeyWindow.Handle, 77);
                }
            }
            catch (Exception e) { results.Add("FAIL  " + e.ToString()); code = 1; }
            finally
            {
                try { if (crash != null && !crash.HasExited) { crash.Kill(); crash.WaitForExit(2000); Thread.Sleep(250); } } catch { }
                try { if (host != null && !host.HasExited) { host.Kill(); host.WaitForExit(2000); } } catch { }
                File.WriteAllLines(output, results.ToArray());
            }
            return code;
        }
    }
}

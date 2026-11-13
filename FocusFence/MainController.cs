using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FocusFence
{
    internal sealed class MainController : IDisposable
    {
        private readonly Window window;
        private readonly Settings settings;
        private readonly GuardEngine engine;
        private readonly bool preview;
        private Forms.NotifyIcon tray;
        private Forms.ToolStripMenuItem trayToggle;
        private DispatcherTimer timer, captureTimer;
        private HwndSource source;
        private bool loading, exiting, disposed, hotkeysReady;
        private int captureSeconds;
        private double accountedSeconds;
        private List<WindowInfo> discovered = new List<WindowInfo>();
        private int pipCandidateCount;
        private bool scanRunning, discoveryCompleted;
        private DateTime lastDiscovery = DateTime.MinValue;
        private string dashboardSignature = "";
        private DateTime lastUi = DateTime.MinValue, lastSave = DateTime.UtcNow;
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        internal bool ReadyForSmokeTest { get { return source != null && hotkeysReady && tray != null && discoveryCompleted && !engine.Armed && !engine.Locked; } }
        internal MainController(Window view, bool isPreview)
        {
            window = view; preview = isPreview;
            settings = preview ? new Settings() : Storage.Load();
            engine = new GuardEngine(!preview);
            engine.Changed += UpdateUi;
            loading = true;
            Wire(); PopulateMonitors(); RefreshProfiles();
            Check("TraySetting").IsChecked = settings.MinimizeToTray;
            Check("MinimizedSetting").IsChecked = settings.StartMinimized;
            Check("SoundSetting").IsChecked = settings.Sounds;
            Check("StartupSetting").IsChecked = IsStartup();
            Check("AutoPipSetting").IsChecked = settings.AutoPip;
            Check("AutoGameSetting").IsChecked = settings.AutoGame;
            SelectTag(Combo("ToggleKeyChoice"), settings.ToggleKey); SelectTag(Combo("PauseKeyChoice"), settings.PauseKey);
            loading = false;
            Navigate("Overview"); UpdateUi();
            window.SourceInitialized += delegate { if (!preview) SetupHotkeys(); };
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!exiting && !preview && settings.MinimizeToTray && tray != null) { e.Cancel = true; window.Hide(); tray.ShowBalloonTip(2500, "FocusFence läuft weiter", "Öffnen oder beenden über das Symbol im Infobereich.", Forms.ToolTipIcon.Info); }
                else { Dispose(); if (!preview) Application.Current.Shutdown(); }
            };
            window.StateChanged += delegate { if (!preview && window.WindowState == WindowState.Minimized && settings.MinimizeToTray) window.Hide(); };
            if (!preview)
            {
                SetupTray();
                Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
                timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += delegate
                {
                    engine.Tick(); AccountTime();
                    if ((DateTime.UtcNow - lastDiscovery).TotalSeconds >= 1.2) RequestDiscovery();
                    if ((DateTime.UtcNow - lastUi).TotalMilliseconds >= 200) { UpdateUi(); lastUi = DateTime.UtcNow; }
                    if ((DateTime.UtcNow - lastSave).TotalMinutes >= 1) { Save(); lastSave = DateTime.UtcNow; }
                };
                timer.Start();
                RequestDiscovery();
                window.Show();
                if (settings.StartMinimized) window.Hide();
                if (!string.IsNullOrEmpty(Storage.LastWarning)) Notify(Storage.LastWarning);
            }
        }
        private T Find<T>(string name) where T : class { return window.FindName(name) as T; }
        private Button Button(string name) { return Find<Button>(name); }
        private TextBlock Text(string name) { return Find<TextBlock>(name); }
        private ComboBox Combo(string name) { return Find<ComboBox>(name); }
        private CheckBox Check(string name) { return Find<CheckBox>(name); }
        private TextBox Input(string name) { return Find<TextBox>(name); }
        private void Click(string name, Action action) { Button(name).Click += delegate { action(); }; }
        private void Wire()
        {
            Click("MinimizeButton", delegate { window.WindowState = WindowState.Minimized; });
            Click("MaximizeButton", delegate { window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
            Click("CloseButton", delegate { window.Close(); });
            foreach (string p in new[] { "Overview", "Profiles", "Settings", "Help" }) { string page = p; Click("Nav" + p, delegate { Navigate(page); }); }
            Click("ToggleGuardButton", Toggle);
            Click("PauseButton", delegate { engine.Pause(); UpdateUi(); });
            Click("GoProfilesButton", delegate { Navigate("Profiles"); RefreshWindows("GameWindows"); });
            Click("RefreshGamesButton", delegate { RefreshWindows("GameWindows"); });
            Click("RefreshPipButton", delegate { RefreshWindows("PipWindows"); });
            Click("PipOptionsButton", delegate { var panel = Find<StackPanel>("PipManualPanel"); panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (panel.Visibility == Visibility.Visible) RefreshWindows("PipWindows"); });
            Click("AutomaticPipButton", delegate { SetAutoPip(true); Find<StackPanel>("PipManualPanel").Visibility = Visibility.Collapsed; ApplyDiscovery(discovered); RequestDiscovery(); });
            Click("ClearPipButton", delegate { SetAutoPip(false); Combo("PipWindows").SelectedIndex = -1; engine.Pip = null; engine.Tick(); UpdateUi(); });
            Click("AddGameButton", delegate { AddGame(Combo("GameWindows").SelectedItem as WindowInfo); });
            Click("CaptureGameButton", Capture);
            Click("SaveProfileButton", SaveProfile);
            Click("DeleteProfileButton", DeleteProfile);
            Click("ExportProfilesButton", Export);
            Click("ImportProfilesButton", Import);
            Click("ExitButton", CloseForReal);
            Click("OpenDataButton", delegate { Directory.CreateDirectory(Storage.Folder); Process.Start("explorer.exe", "\"" + Storage.Folder + "\""); });
            Combo("DashboardProfiles").SelectionChanged += delegate { SelectProfile(Combo("DashboardProfiles")); };
            Combo("EditProfiles").SelectionChanged += delegate { SelectProfile(Combo("EditProfiles")); };
            Combo("PipWindows").SelectionChanged += delegate
            {
                if (loading) return;
                engine.Pip = Combo("PipWindows").SelectedItem as WindowInfo;
                if (engine.Pip != null) SetAutoPip(false);
                engine.Tick(); UpdateUi();
                if (engine.Pip != null) Notify("Videofenster gewählt. Der Klickschutz beginnt, sobald dein Spiel aktiv ist.");
            };
            Find<Slider>("InsetSlider").ValueChanged += delegate { Text("InsetLabel").Text = "Randabstand · " + (int)Find<Slider>("InsetSlider").Value + " px"; };
            Combo("AreaChoice").SelectionChanged += delegate { Combo("MonitorChoice").IsEnabled = Combo("AreaChoice").SelectedIndex == 1; };
            foreach (string key in new[] { "TraySetting", "MinimizedSetting", "SoundSetting", "StartupSetting", "AutoPipSetting", "AutoGameSetting" })
            { Check(key).Checked += SettingsChanged; Check(key).Unchecked += SettingsChanged; }
            Combo("ToggleKeyChoice").SelectionChanged += KeysChanged;
            Combo("PauseKeyChoice").SelectionChanged += KeysChanged;
        }
        private void Navigate(string page)
        {
            foreach (string p in new[] { "Overview", "Profiles", "Settings", "Help" })
            {
                Find<StackPanel>(p + "Page").Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
                Button("Nav" + p).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(p == page ? "#303236" : "#1C1D20"));
                Button("Nav" + p).Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(p == page ? "#FAFAFA" : "#A5A7AD"));
                Button("Nav" + p).FontWeight = p == page ? FontWeights.SemiBold : FontWeights.Normal;
            }
            string title = "Mausschutz", sub = "Einschalten. Ins Spiel wechseln.";
            if (page == "Profiles") { title = "Spielprofile"; sub = "Spiele und ihre Sperrbereiche verwalten."; }
            if (page == "Settings") { title = "Einstellungen"; sub = "Tastenkürzel und Programmverhalten anpassen."; }
            if (page == "Help") { title = "Hilfe"; sub = "Einrichtung, Freigabe und häufige Fragen."; }
            Text("PageTitle").Text = title; Text("PageSubtitle").Text = sub;
            Find<ScrollViewer>("PageScroll").ScrollToTop();
            Text("PageEyebrow").Text = page == "Overview" ? "FOCUS CONTROL" : page == "Profiles" ? "DEINE SPIELPROFILE" : page == "Settings" ? "DEINE EINSTELLUNGEN" : "QUICK START";
            Text("LifetimeText").Text = "Insgesamt " + FormatTime(settings.TotalSeconds) + " geschützte Spielzeit in " + settings.Sessions + " Sessions. Profile, Einstellungen und Ereignisse liegen lokal in deinem Datenordner.";
        }
        private void RefreshProfiles()
        {
            bool before = loading; loading = true;
            Combo("EditProfiles").ItemsSource = null;
            Combo("EditProfiles").ItemsSource = settings.Profiles;
            int index = settings.SelectedProfile;
            if (index < 0 || index >= settings.Profiles.Count) index = settings.Profiles.Count > 0 ? 0 : -1;
            settings.SelectedProfile = index;
            Combo("EditProfiles").SelectedIndex = index;
            engine.ChangeProfile(index >= 0 ? settings.Profiles[index] : null);
            RefreshDashboardChoices();
            loading = before; FillEditor();
        }
        private void SelectProfile(ComboBox combo)
        {
            if (loading) return;
            AccountTime(); engine.Stop();
            var profile = combo.SelectedItem as GameProfile;
            if (profile != null && !settings.Profiles.Contains(profile)) settings.Profiles.Add(profile);
            settings.SelectedProfile = profile == null ? -1 : settings.Profiles.IndexOf(profile); RefreshProfiles(); Save(); UpdateUi();
        }
        private void FillEditor()
        {
            var p = engine.Profile;
            Input("ProfileName").Text = p == null ? "" : p.Name;
            Input("ProcessName").Text = p == null ? "" : p.ProcessName + ".exe";
            Combo("AreaChoice").SelectedIndex = p != null && p.Area == "monitor" ? 1 : 0;
            Find<Slider>("InsetSlider").Value = p == null ? 0 : p.Inset;
            Check("PipEnabled").IsChecked = p == null || p.PipClickThrough;
            Combo("MonitorChoice").SelectedIndex = 0;
            if (p != null) foreach (ComboBoxItem item in Combo("MonitorChoice").Items) if ((string)item.Tag == p.Monitor) Combo("MonitorChoice").SelectedItem = item;
            Button("SaveProfileButton").IsEnabled = p != null; Button("DeleteProfileButton").IsEnabled = p != null;
            Find<StackPanel>("ProfileEditor").Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
            Text("NoProfilesText").Visibility = p == null ? Visibility.Visible : Visibility.Collapsed;
            Combo("EditProfiles").Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
        }
        private void PopulateMonitors()
        {
            var combo = Combo("MonitorChoice"); combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "Automatisch · Monitor des Spiels", Tag = "" });
            int i = 0;
            foreach (var s in Forms.Screen.AllScreens) combo.Items.Add(new ComboBoxItem { Content = "Monitor " + (++i) + " · " + s.Bounds.Width + " × " + s.Bounds.Height + (s.Primary ? " · Hauptmonitor" : ""), Tag = s.DeviceName });
            combo.SelectedIndex = 0;
        }
        private static string FriendlyBrowser(string process)
        {
            switch ((process ?? "").ToLowerInvariant())
            {
                case "chrome": return "Chrome"; case "msedge": return "Edge"; case "firefox": return "Firefox";
                case "brave": return "Brave"; case "opera": return "Opera"; case "vivaldi": return "Vivaldi";
                default: return string.IsNullOrEmpty(process) ? "Video" : char.ToUpperInvariant(process[0]) + process.Substring(1);
            }
        }
        private void SetAutoPip(bool enabled)
        {
            settings.AutoPip = enabled;
            bool before = loading; loading = true; Check("AutoPipSetting").IsChecked = enabled; loading = before;
            Save(); UpdateUi();
        }
        private void RequestDiscovery()
        {
            if (preview || disposed || scanRunning) return;
            scanRunning = true; lastDiscovery = DateTime.UtcNow;
            Task.Factory.StartNew(delegate { return Native.Windows(); }).ContinueWith(delegate(Task<List<WindowInfo>> task)
            {
                if (task.IsFaulted) { Storage.Log("Fenstersuche: " + task.Exception.GetBaseException().Message); }
                if (window.Dispatcher.HasShutdownStarted) return;
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    scanRunning = false;
                    if (!disposed && task.Status == TaskStatus.RanToCompletion) ApplyDiscovery(task.Result);
                }));
            });
        }
        internal void ApplyDiscovery(List<WindowInfo> windows)
        {
            if (disposed) return;
            discoveryCompleted = true;
            discovered = windows;
            var candidates = WindowDiscovery.PipCandidates(windows); pipCandidateCount = candidates.Count;
            if (settings.AutoPip) engine.Pip = WindowDiscovery.ChoosePip(candidates, engine.Pip);
            else if (engine.Pip != null && !windows.Any(delegate(WindowInfo w) { return WindowDiscovery.Same(w, engine.Pip); })) engine.Pip = null;
            if (settings.AutoGame)
            {
                var game = WindowDiscovery.ChooseGame(windows, Native.GetForegroundWindow(), engine.Profile, settings.Profiles);
                if (game != null && (engine.Profile == null || !string.Equals(engine.Profile.ProcessName, game.ProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    var profile = settings.Profiles.FirstOrDefault(delegate(GameProfile p) { return string.Equals(p.ProcessName, game.ProcessName, StringComparison.OrdinalIgnoreCase); });
                    if (profile == null) { profile = new GameProfile { Name = WindowDiscovery.GameName(game) ?? game.Title.Trim(), ProcessName = game.ProcessName }; settings.Profiles.Add(profile); }
                    settings.SelectedProfile = settings.Profiles.IndexOf(profile); RefreshProfiles(); Save();
                }
            }
            string signature = string.Join("|", windows.Select(delegate(WindowInfo w) { return w.Handle + ":" + w.ProcessId + ":" + w.Title; }));
            if (signature != dashboardSignature && !Combo("DashboardProfiles").IsDropDownOpen)
            { dashboardSignature = signature; RefreshDashboardChoices(); }
            engine.Tick(); UpdateUi();
        }
        private void RefreshDashboardChoices()
        {
            var choices = new List<GameProfile>(settings.Profiles);
            foreach (var w in discovered.OrderByDescending(delegate(WindowInfo item) { return WindowDiscovery.GameName(item) != null; }))
            {
                if (WindowDiscovery.IsBrowser(w.ProcessName) || !w.Visible || w.Cloaked || w.Minimized || w.Bounds.Width < 240 || w.Bounds.Height < 180 || string.IsNullOrWhiteSpace(w.Title)) continue;
                if (choices.Any(delegate(GameProfile p) { return string.Equals(p.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase); })) continue;
                string name = WindowDiscovery.GameName(w) ?? w.Title.Trim();
                choices.Add(new GameProfile { Name = name.Length > 65 ? name.Substring(0, 62) + "…" : name, ProcessName = w.ProcessName });
            }
            bool before = loading; loading = true;
            Combo("DashboardProfiles").ItemsSource = choices;
            Combo("DashboardProfiles").SelectedItem = engine.Profile;
            loading = before;
        }
        private void RefreshWindows(string name)
        {
            var combo = Combo(name); var selected = combo.SelectedItem as WindowInfo;
            if (name == "PipWindows" && selected == null) selected = engine.Pip;
            var list = Native.Windows();
            bool before = loading; loading = true;
            combo.ItemsSource = list;
            if (selected != null) combo.SelectedItem = list.FirstOrDefault(delegate(WindowInfo w) { return WindowDiscovery.Same(w, selected); });
            loading = before;
            Notify(list.Count == 0 ? "Keine Fenster gefunden. Öffne zuerst dein Spiel bzw. das Bild-in-Bild-Fenster." : list.Count + " Fenster gefunden. Bitte das gewünschte Fenster auswählen.");
        }
        private void AddGame(WindowInfo w)
        {
            if (w == null || !Native.IsWindow(w.Handle) || w.ProcessId == Process.GetCurrentProcess().Id) { Notify("Bitte zuerst ein gültiges Spielfenster auswählen."); return; }
            var existing = settings.Profiles.FindIndex(delegate(GameProfile p) { return string.Equals(p.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase); });
            if (existing < 0)
            {
                settings.Profiles.Add(new GameProfile { Name = w.Title.Length > 60 ? w.Title.Substring(0, 60) : w.Title, ProcessName = w.ProcessName });
                existing = settings.Profiles.Count - 1;
            }
            AccountTime(); engine.Stop(); settings.SelectedProfile = existing; RefreshProfiles(); Save(); UpdateUi(); Notify("Spielprofil gespeichert. Du kannst den Namen und Sperrbereich unten anpassen.");
        }
        private void Capture()
        {
            if (captureTimer != null) return;
            captureSeconds = 5; Text("CaptureText").Text = "Wechsle jetzt ins Spiel · Erfassung in 5 Sekunden …";
            Button("CaptureGameButton").IsEnabled = false;
            captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            captureTimer.Tick += delegate
            {
                captureSeconds--;
                Text("CaptureText").Text = "Erfassung in " + captureSeconds + " Sekunden …";
                if (captureSeconds > 0) return;
                captureTimer.Stop(); captureTimer = null; Button("CaptureGameButton").IsEnabled = true;
                var w = Native.Describe(Native.GetForegroundWindow());
                AddGame(w); Text("CaptureText").Text = w != null && w.ProcessId != Process.GetCurrentProcess().Id ? "Erfasst: " + w.Title : "Kein Spiel erfasst. Wechsle während des Countdowns ins Spiel.";
            };
            captureTimer.Start();
        }
        private void SaveProfile()
        {
            var p = engine.Profile; if (p == null) return;
            if (string.IsNullOrWhiteSpace(Input("ProfileName").Text)) { Notify("Bitte gib deinem Profil einen Namen."); return; }
            AccountTime(); engine.Stop();
            p.Name = Input("ProfileName").Text.Trim(); p.Area = Combo("AreaChoice").SelectedIndex == 1 ? "monitor" : "window";
            p.Inset = (int)Find<Slider>("InsetSlider").Value; p.PipClickThrough = Check("PipEnabled").IsChecked == true;
            var item = Combo("MonitorChoice").SelectedItem as ComboBoxItem; p.Monitor = item == null ? "" : (string)item.Tag;
            RefreshProfiles(); Save(); UpdateUi(); Notify("Profil gespeichert. Aktiviere den Schutz, wenn du bereit bist.");
        }
        private void DeleteProfile()
        {
            if (engine.Profile == null) return;
            AccountTime(); engine.Stop(); settings.Profiles.Remove(engine.Profile); settings.SelectedProfile = -1;
            RefreshProfiles(); Save(); UpdateUi(); Notify("Profil gelöscht.");
        }
        private void Export()
        {
            var dialog = new SaveFileDialog { Title = "Spielprofile exportieren", FileName = "FocusFence-Profile.xml", Filter = "FocusFence-Profile (*.xml)|*.xml" };
            if (dialog.ShowDialog(window) != true) return;
            try { Storage.Write(dialog.FileName, settings.Profiles); Notify("Profile exportiert."); }
            catch (Exception e) { Notify("Export fehlgeschlagen: " + e.Message); }
        }
        private void Import()
        {
            var dialog = new OpenFileDialog { Title = "Spielprofile importieren", Filter = "FocusFence-Profile (*.xml)|*.xml" };
            if (dialog.ShowDialog(window) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("Die Datei ist zu groß.");
                var profiles = Storage.Read<List<GameProfile>>(dialog.FileName); int count = 0;
                if (profiles == null || profiles.Count > 200) throw new InvalidDataException("Ungültige Profildatei.");
                foreach (var p in profiles)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.ProcessName) || p.ProcessName.Length > 260) continue;
                    Storage.Validate(p);
                    if (settings.Profiles.Any(delegate(GameProfile x) { return string.Equals(x.ProcessName, p.ProcessName, StringComparison.OrdinalIgnoreCase); })) continue;
                    settings.Profiles.Add(p); count++;
                }
                RefreshProfiles(); Save(); Notify(count + " Profile importiert. Vorhandene Profile wurden beibehalten.");
            }
            catch (Exception e) { Notify("Import fehlgeschlagen: " + e.Message); }
        }
        private void Toggle()
        {
            if (engine.Profile == null && !settings.AutoGame) { Combo("DashboardProfiles").Focus(); Combo("DashboardProfiles").IsDropDownOpen = true; Notify("Wähle dein Spiel aus der Liste."); return; }
            if (!engine.Armed && !hotkeysReady && !preview) { Notify("Die Notfall-Tastenkombination ist belegt. Ändere die Belegung im anderen Programm und starte FocusFence neu."); return; }
            AccountTime();
            if (!engine.Armed) { settings.Sessions++; accountedSeconds = 0; }
            engine.Toggle();
            if (settings.Sounds) SystemSounds.Asterisk.Play();
            Save(); UpdateUi();
        }
        internal void EmergencyStop() { AccountTime(); engine.Stop(); Save(); UpdateUi(); }
        private void AccountTime()
        {
            if (engine.SessionSeconds >= accountedSeconds) settings.TotalSeconds += engine.SessionSeconds - accountedSeconds;
            accountedSeconds = engine.SessionSeconds;
        }
        private static string FormatTime(double seconds) { var span = TimeSpan.FromSeconds(Math.Max(0, seconds)); return ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00"); }
        private void UpdateUi()
        {
            if (disposed) return;
            bool pause = engine.Armed && engine.PauseUntil > DateTime.UtcNow;
            Text("StatusBadge").Text = engine.Locked ? "Aktiv" : pause ? "Pausiert" : engine.Armed ? "Bereit" : "Ausgeschaltet";
            Find<System.Windows.Shapes.Ellipse>("StatusDot").Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(engine.Locked ? "#A8D9BB" : engine.Armed ? "#D8CCAA" : "#747985"));
            Text("GameDiscoveryHint").Text = settings.AutoGame ? "Erkennt bekannte Spiele automatisch" : "Spiel direkt aus der Liste wählen";
            Text("HeroTitle").Text = engine.Locked ? "Maus im Spiel." : pause ? "Kurz freigegeben." : engine.Armed ? "Schutz ist bereit." : "Maus frei.";
            Text("HeroDescription").Text = engine.Locked ? "Mit Alt+Tab wechselst du wie gewohnt." : pause ? "Noch " + Math.Max(0, Math.Ceiling((engine.PauseUntil - DateTime.UtcNow).TotalSeconds)) + " Sekunden Pause." : engine.Armed ? engine.Profile == null ? "Wechsle in dein Spiel. Bekannte Spiele erkennen wir automatisch." : "Wechsle zu " + engine.Profile.Name + "." : "Ein Klick, dann kannst du weiterspielen.";
            Button("ToggleGuardButton").Content = engine.Armed ? "Ausschalten" : "Einschalten";
            Button("PauseButton").IsEnabled = engine.Armed; Button("PauseButton").Content = pause ? "Jetzt fortsetzen" : "30 s freigeben";
            Text("ToggleHint").Text = "Strg + Alt + " + settings.ToggleKey;
            Text("SessionText").Text = FormatTime(engine.SessionSeconds);
            Text("BoundsText").Text = engine.Locked ? engine.CurrentBounds.ToString() : engine.Profile != null && engine.Profile.Area == "monitor" ? "Monitor" : "Spielfenster";
            Text("BoundsText").FontSize = 13;
            Text("BoundsSubtext").Text = engine.Profile == null ? "wähle zuerst ein Spiel" : engine.Profile.Inset > 0 ? engine.Profile.Inset + " px Abstand zum Rand" : "passt sich automatisch an";
            Text("PipStatusText").Text = engine.Pip == null ? "Nicht gewählt" : engine.Locked && engine.Profile != null && engine.Profile.PipClickThrough ? "Klickgeschützt" : "Bereit";
            Text("PipWindowTitle").Text = engine.Pip == null ? settings.AutoPip ? pipCandidateCount > 1 ? pipCandidateCount + " Videofenster gefunden" : "Warte auf dein Video" : "Automatik ausgeschaltet" : FriendlyBrowser(engine.Pip.ProcessName) + " · " + (settings.AutoPip ? "automatisch erkannt" : "manuell gewählt");
            Text("PipHint").Text = engine.Pip == null ? settings.AutoPip ? pipCandidateCount > 1 ? "Wähle unter Ändern das gewünschte Video." : "Öffne ein Bild-in-Bild-Video im Browser." : "Wähle ein Fenster oder schalte die Automatik ein." : engine.Locked && engine.Profile != null && engine.Profile.PipClickThrough ? "Klicks gehen durch das Video. Alt+Tab gibt es frei." : engine.Profile != null && !engine.Profile.PipClickThrough ? "Klickschutz ist in diesem Spielprofil ausgeschaltet." : "Wird beim Spielen automatisch klickgeschützt.";
            Find<System.Windows.Shapes.Ellipse>("PipDot").Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(engine.Pip == null ? "#62656E" : "#A8D9BB"));
            Text("FooterStatus").Text = engine.Status;
            if (tray != null) { tray.Text = "FocusFence · " + (engine.Locked ? "Geschützt" : engine.Armed ? "Bereit" : "Schutz aus"); trayToggle.Text = engine.Armed ? "Schutz deaktivieren" : "Schutz aktivieren"; }
        }
        private void Notify(string message) { Text("FooterStatus").Text = message; lastUi = DateTime.UtcNow.AddSeconds(5); }
        private void Save() { if (!preview && !Storage.Save(settings)) Notify(Storage.LastWarning); }
        private void SetupTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("FocusFence öffnen", null, delegate { Show(); });
            trayToggle = new Forms.ToolStripMenuItem("Schutz aktivieren", null, delegate { Toggle(); }); menu.Items.Add(trayToggle);
            menu.Items.Add("30 s Pause / fortsetzen", null, delegate { engine.Pause(); UpdateUi(); });
            menu.Items.Add("Sofort freigeben", null, delegate { EmergencyStop(); }); menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Beenden", null, delegate { CloseForReal(); });
            System.Drawing.Icon icon;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath); }
            catch { icon = System.Drawing.SystemIcons.Application; }
            tray = new Forms.NotifyIcon { Icon = icon, Text = "FocusFence · Schutz aus", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Show(); };
        }
        private void Show() { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); }
        private void SetupHotkeys()
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle); source.AddHook(MessageHook);
            hotkeysReady = Native.RegisterHotKey(source.Handle, 3, 0x4003, 0x7B); // Ctrl+Alt+F12, no repeat
            RegisterKeys();
        }
        private void RegisterKeys()
        {
            if (source == null) return;
            Native.UnregisterHotKey(source.Handle, 1); Native.UnregisterHotKey(source.Handle, 2);
            bool a = Native.RegisterHotKey(source.Handle, 1, 0x4003, KeyCode(settings.ToggleKey));
            bool b = Native.RegisterHotKey(source.Handle, 2, 0x4003, KeyCode(settings.PauseKey));
            Text("HotkeyStatus").Text = !hotkeysReady ? "Notfall-Freigabe ist bereits belegt. Der Schutz bleibt aus." : a && b ? "✓  Alle Tastenkürzel sind verfügbar." : "Ein Tastenkürzel ist belegt. Wähle oben eine andere Taste; die Notfall-Freigabe ist verfügbar.";
            if (!a || !b || !hotkeysReady) Notify(Text("HotkeyStatus").Text);
        }
        private static uint KeyCode(string key) { return (uint)(0x6F + int.Parse(key.Substring(1))); }
        private IntPtr MessageHook(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == 0x0312)
            {
                int id = wparam.ToInt32(); handled = true;
                if (id == 1) Toggle(); else if (id == 2) { engine.Pause(); UpdateUi(); } else if (id == 3) { EmergencyStop(); Notify("Notfall-Freigabe · Schutz aus, Maus und Videofenster frei."); }
            }
            return IntPtr.Zero;
        }
        private static void SelectTag(ComboBox combo, string tag) { foreach (ComboBoxItem item in combo.Items) if ((string)item.Tag == tag) combo.SelectedItem = item; }
        private void KeysChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading) return;
            settings.ToggleKey = (string)((ComboBoxItem)Combo("ToggleKeyChoice").SelectedItem).Tag;
            settings.PauseKey = (string)((ComboBoxItem)Combo("PauseKeyChoice").SelectedItem).Tag;
            RegisterKeys(); Save(); UpdateUi();
        }
        private bool IsStartup() { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("FocusFence") != null; }
        private void SettingsChanged(object sender, RoutedEventArgs e)
        {
            if (loading || preview) return;
            settings.MinimizeToTray = Check("TraySetting").IsChecked == true;
            settings.StartMinimized = Check("MinimizedSetting").IsChecked == true;
            settings.Sounds = Check("SoundSetting").IsChecked == true;
            settings.AutoGame = Check("AutoGameSetting").IsChecked == true;
            if (sender == Check("AutoPipSetting"))
            {
                settings.AutoPip = Check("AutoPipSetting").IsChecked == true;
                if (!settings.AutoPip) engine.Pip = null;
                ApplyDiscovery(discovered);
            }
            if (sender == Check("StartupSetting"))
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    { if (Check("StartupSetting").IsChecked == true) key.SetValue("FocusFence", "\"" + Forms.Application.ExecutablePath + "\""); else key.DeleteValue("FocusFence", false); }
                }
                catch (Exception ex) { loading = true; Check("StartupSetting").IsChecked = IsStartup(); loading = false; Notify("Autostart konnte nicht geändert werden: " + ex.Message); }
            }
            Save();
        }
        private void SessionChanged(object sender, SessionSwitchEventArgs e)
        { if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff) window.Dispatcher.BeginInvoke(new Action(EmergencyStop)); }
        private void DisplaysChanged(object sender, EventArgs e)
        { window.Dispatcher.BeginInvoke(new Action(delegate { EmergencyStop(); PopulateMonitors(); FillEditor(); Notify("Monitore geändert. Bitte Bereich prüfen und Schutz neu aktivieren."); })); }
        internal void CloseForReal() { exiting = true; window.Close(); }
        public void Dispose()
        {
            if (disposed) return;
            AccountTime(); engine.Dispose(); Save(); disposed = true;
            if (timer != null) timer.Stop(); if (captureTimer != null) captureTimer.Stop();
            Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            if (source != null) { for (int id = 1; id <= 3; id++) Native.UnregisterHotKey(source.Handle, id); source.RemoveHook(MessageHook); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
        }
    }
}

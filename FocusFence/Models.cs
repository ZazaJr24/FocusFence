using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace FocusFence
{
    public class GameProfile
    {
        public string Name = "My game";
        public string ProcessName = "";
        public string Area = "window";
        public string Monitor = "";
        public int Inset = 0;
        public bool PipClickThrough = true;
        public override string ToString() { return Name; }
    }
    public class Settings
    {
        public List<GameProfile> Profiles = new List<GameProfile>();
        public int SelectedProfile = -1;
        public bool MinimizeToTray = true;
        public bool StartMinimized = false;
        public bool Sounds = false;
        public bool AutoPip = true;
        public bool AutoGame = true;
        public string ToggleKey = "F8";
        public string PauseKey = "F9";
        public double TotalSeconds = 0;
        public int Sessions = 0;
    }
    internal static class Storage
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusFence");
        internal static string SettingsPath { get { return Path.Combine(Folder, "settings.xml"); } }
        internal static string LastWarning = "";
        internal static T Read<T>(string path) where T : new()
        {
            if (!File.Exists(path)) return new T();
            using (var stream = File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(stream);
        }
        internal static Settings Load()
        {
            try
            {
                var s = Read<Settings>(SettingsPath);
                if (s.Profiles == null) s.Profiles = new List<GameProfile>();
                s.Profiles.RemoveAll(delegate(GameProfile p) { return p == null || string.IsNullOrWhiteSpace(p.ProcessName); });
                foreach (var p in s.Profiles) Validate(p);
                if (s.ToggleKey != "F8" && s.ToggleKey != "F6" && s.ToggleKey != "F10") s.ToggleKey = "F8";
                if (s.PauseKey != "F9" && s.PauseKey != "F7" && s.PauseKey != "F11") s.PauseKey = "F9";
                return s;
            }
            catch (Exception e) { LastWarning = "Could not load settings: " + e.Message; Log(LastWarning); return new Settings(); }
        }
        internal static void Validate(GameProfile p)
        {
            if (p.Area != "window" && p.Area != "monitor") p.Area = "window";
            p.Inset = Math.Max(0, Math.Min(50, p.Inset));
            if (p.Name == null) p.Name = p.ProcessName;
        }
        internal static void Write<T>(string path, T data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (var stream = File.Create(tmp)) new XmlSerializer(typeof(T)).Serialize(stream, data);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
        internal static bool Save(Settings settings)
        {
            try { Write(SettingsPath, settings); return true; }
            catch (Exception e) { LastWarning = "Could not save settings: " + e.Message; Log(LastWarning); return false; }
        }
        internal static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "activity.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + "." + DateTime.Now.ToString("yyyyMMddHHmmss"));
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FocusFence
{
    internal static class WindowDiscovery
    {
        private static readonly HashSet<string> Browsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "chromium", "waterfox", "librewolf", "zen", "thorium", "arc", "floorp" };
        private static readonly HashSet<string> PipTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "pictureinpicture", "bildinbild", "bildimbild", "incrustationvidéo", "incrustationvideo", "imagenenimagen", "imagememimagem", "immaginenellimmagine", "obrazwobrazie", "картинкавкартинке", "resimiçinderesim", "resimicinderesim", "画中画", "子母畫面", "ピクチャーインピクチャー", "화면속화면" };
        private static readonly Dictionary<string, string> Games = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "FortniteClient-Win64-Shipping", "Fortnite" }, { "VALORANT-Win64-Shipping", "VALORANT" }, { "cs2", "Counter-Strike 2" },
            { "r5apex", "Apex Legends" }, { "RocketLeague", "Rocket League" }, { "Overwatch", "Overwatch" }, { "League of Legends", "League of Legends" },
            { "GTA5", "Grand Theft Auto V" }, { "GTA5_Enhanced", "Grand Theft Auto V" }, { "RainbowSix", "Rainbow Six Siege" }, { "RainbowSix_Vulkan", "Rainbow Six Siege" },
            { "Minecraft.Windows", "Minecraft" }, { "RobloxPlayerBeta", "Roblox" }, { "RustClient", "Rust" }, { "TslGame", "PUBG" },
            { "EscapeFromTarkov", "Escape from Tarkov" }, { "cod", "Call of Duty" }, { "dota2", "Dota 2" }, { "bf6", "Battlefield" }, { "DeadByDaylight-Win64-Shipping", "Dead by Daylight" }
        };
        internal static bool IsBrowser(string process) { return Browsers.Contains(process ?? ""); }
        private static string Normalize(string title)
        {
            var text = new StringBuilder();
            foreach (char c in title ?? "") if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
            return text.ToString();
        }
        internal static int PipScore(WindowInfo w)
        {
            if (w == null || !w.Visible || w.Minimized || w.Cloaked || !IsBrowser(w.ProcessName)) return 0;
            bool chromium = w.WindowClass.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);
            bool mozilla = w.WindowClass == "MozillaDialogClass" || w.WindowClass == "MozillaWindowClass";
            if (!chromium && !mozilla) return 0;
            bool top = (w.ExtendedStyle & 8) != 0;
            bool named = PipTitles.Contains(Normalize(w.Title));
            // Native browser class + title + always-on-top. Never classify an ordinary browser tab by a substring.
            if (named && top) return 100;
            // Document PiP may have a page-defined title. Require a compact, topmost browser tool/owned window without min/max buttons.
            bool compact = w.Bounds.Width >= 120 && w.Bounds.Height >= 60 && w.MonitorBounds.Width > 0 && w.MonitorBounds.Height > 0
                && (long)w.Bounds.Width * w.Bounds.Height < (long)w.MonitorBounds.Width * w.MonitorBounds.Height * 0.6;
            bool tool = (w.ExtendedStyle & 0x80) != 0 || w.Owner != IntPtr.Zero;
            bool noMinMax = (w.Style & 0x30000) == 0;
            return top && compact && tool && noMinMax ? 85 : 0;
        }
        internal static List<WindowInfo> PipCandidates(IEnumerable<WindowInfo> windows)
        {
            return windows.Where(delegate(WindowInfo w) { return PipScore(w) > 0; }).OrderByDescending(PipScore).ThenBy(delegate(WindowInfo w) { return w.Handle.ToInt64(); }).ToList();
        }
        internal static bool Same(WindowInfo a, WindowInfo b) { return a != null && b != null && a.Handle == b.Handle && a.ProcessId == b.ProcessId; }
        internal static WindowInfo ChoosePip(List<WindowInfo> candidates, WindowInfo current)
        {
            var retained = candidates.FirstOrDefault(delegate(WindowInfo w) { return Same(w, current); });
            if (retained != null) return retained;
            return candidates.Count == 1 ? candidates[0] : null;
        }
        internal static string GameName(WindowInfo w)
        {
            if (w == null || !w.Visible || w.Minimized || w.Cloaked || IsBrowser(w.ProcessName)) return null;
            string name;
            if (Games.TryGetValue(w.ProcessName, out name)) return name;
            if ((w.ProcessName == "javaw" || w.ProcessName == "java") && (w.Title ?? "").StartsWith("Minecraft", StringComparison.OrdinalIgnoreCase)) return "Minecraft";
            return null;
        }
        internal static WindowInfo ChooseGame(IList<WindowInfo> windows, IntPtr foreground, GameProfile current, IList<GameProfile> profiles)
        {
            // Keep a manually selected profile. Only follow known/saved games when their actual window is foreground.
            var active = windows.FirstOrDefault(delegate(WindowInfo w) { return w.Handle == foreground && (GameName(w) != null || profiles.Any(delegate(GameProfile p) { return string.Equals(p.ProcessName, w.ProcessName, StringComparison.OrdinalIgnoreCase); })); });
            if (active != null) return active;
            if (current != null) return null;
            var known = windows.Where(delegate(WindowInfo w) { return GameName(w) != null; }).ToList();
            return known.Count == 1 ? known[0] : null;
        }
    }
}

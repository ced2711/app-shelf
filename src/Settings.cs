// User settings, saved to %APPDATA%\AppShelf\settings.ini, plus the "Start with Windows" registry entry.

using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppShelf
{
    class Settings
    {
        public const int MinColumns = 3, MaxColumns = 12;

        public int Columns;                 // 0 = fit to the window
        public int IconSize = 1;            // 0 small, 1 medium, 2 large
        public bool Sections = true;        // A-Z section headers
        public int Theme;                   // 0 follow Windows, 1 light, 2 dark
        public bool MinimizeToTray;
        public bool CloseToTray;
        public Rectangle Bounds;            // last normal window bounds (empty = centered default)
        public bool Maximized;

        public static Settings Current = Load();

        public static string PathOverride { get; set; }     // self-test only

        public static string FilePath
        {
            get { return PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppShelf", "settings.ini"); }
        }

        public bool UsesTray { get { return MinimizeToTray || CloseToTray || Autostart.Get(); } }

        public int IconPixels { get { return IconSize == 0 ? 32 : IconSize == 2 ? 64 : 48; } }

        public bool? DarkOverride { get { return Theme == 1 ? false : Theme == 2 ? (bool?)true : null; } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                int x = 0, y = 0, w = 0, h = 0;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n;
                    int.TryParse(v, out n);
                    bool b = v == "1";
                    switch (k)
                    {
                        case "columns": s.Columns = n == 0 ? 0 : Clamp(n, MinColumns, MaxColumns); break;
                        case "iconSize": s.IconSize = Clamp(n, 0, 2); break;
                        case "sections": s.Sections = b; break;
                        case "theme": s.Theme = Clamp(n, 0, 2); break;
                        case "minimizeToTray": s.MinimizeToTray = b; break;
                        case "closeToTray": s.CloseToTray = b; break;
                        case "x": x = n; break;
                        case "y": y = n; break;
                        case "width": w = n; break;
                        case "height": h = n; break;
                        case "maximized": s.Maximized = b; break;
                    }
                }
                if (w > 0 && h > 0) s.Bounds = new Rectangle(x, y, w, h);
            }
            catch { }
            return s;
        }

        // Returns false if the file could not be written.
        public bool Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("columns=" + Columns);
                sb.AppendLine("iconSize=" + IconSize);
                sb.AppendLine("sections=" + (Sections ? 1 : 0));
                sb.AppendLine("theme=" + Theme);
                sb.AppendLine("minimizeToTray=" + (MinimizeToTray ? 1 : 0));
                sb.AppendLine("closeToTray=" + (CloseToTray ? 1 : 0));
                if (!Bounds.IsEmpty)
                {
                    sb.AppendLine("x=" + Bounds.X);
                    sb.AppendLine("y=" + Bounds.Y);
                    sb.AppendLine("width=" + Bounds.Width);
                    sb.AppendLine("height=" + Bounds.Height);
                }
                sb.AppendLine("maximized=" + (Maximized ? 1 : 0));
                // Write a temp file first and swap it in, so a crash or power cut mid-write never leaves
                // a half-written or empty settings file behind (which would reset everything to defaults).
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                return true;
            }
            catch { return false; }
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }

    // HKCU Run entry. Started this way, AppShelf opens quietly in the tray.
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "AppShelf";
        public const string TrayArg = "--tray";

        public static bool Get()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" " + TrayArg);
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}

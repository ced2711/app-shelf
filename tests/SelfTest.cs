// Console self-test, compiled only by build.cmd test (defines SELFTEST).
// Usage: AppShelfTest.exe <output folder>
// Exit code 0 = all checks passed.

#if SELFTEST
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AppShelf
{
    static class SelfTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine("  [{0}] {1}", ok ? "ok" : "FAIL", what);
            if (!ok) failures++;
        }

        public static void Run(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "appshelf-test");
            Directory.CreateDirectory(outDir);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var apps = Shell.EnumerateApps();
            Console.WriteLine("apps: {0} ({1} ms)", apps.Count, sw.ElapsedMilliseconds);
            Check(apps.Count > 0, "Start > All apps is not empty");

            sw.Restart();
            int withIcon = 0;
            foreach (var a in apps) { a.Icon = Shell.GetIcon(a.ShellPath, 48); if (a.Icon != null) withIcon++; }
            Console.WriteLine("icons: {0}/{1} ({2} ms)", withIcon, apps.Count, sw.ElapsedMilliseconds);
            Check(withIcon > apps.Count * 0.9, "at least 90% of apps have an icon");

            // contact sheet of the first 40 icons on a checkerboard (to check transparency)
            using (var sheet = new Bitmap(10 * 56, 4 * 56))
            using (var g = Graphics.FromImage(sheet))
            {
                for (int y = 0; y < sheet.Height; y += 8)
                    for (int x = 0; x < sheet.Width; x += 8)
                        g.FillRectangle(((x + y) / 8) % 2 == 0 ? Brushes.White : Brushes.LightGray, x, y, 8, 8);
                for (int i = 0; i < Math.Min(40, apps.Count); i++)
                    if (apps[i].Icon != null) g.DrawImage(apps[i].Icon, (i % 10) * 56 + 4, (i / 10) * 56 + 4, 48, 48);
                sheet.Save(Path.Combine(outDir, "icons.png"));
            }
            using (var logo = Logo.Render(256)) logo.Save(Path.Combine(outDir, "logo.png"));

            var desktopApp = apps.FirstOrDefault(a => !a.IsStoreApp && a.HasTargetFile && AppInfo.FindUninstall(a) != null);
            var storeApp = apps.FirstOrDefault(a => a.IsStoreApp);
            var idListApp = apps.FirstOrDefault(a => !a.IsStoreApp && AppInfo.FindStartMenuShortcut(a) == null);
            foreach (var a in new[] { desktopApp, storeApp, idListApp })
            {
                if (a == null) continue;
                Console.WriteLine("\n=== " + a.Name);
                var details = AppInfo.GetDetails(a);
                foreach (var d in details) Console.WriteLine("  {0,-13} {1,-20} {2}", d.Section, d.Key, d.Value);
                Check(details.Count > 0 && details[0].Key == "Name" && details[0].Section == "App", "details start with the App section");
                string path;
                bool created = Actions.CreateShortcut(a, outDir, out path);
                Check(created && File.Exists(path) && new FileInfo(path).Length > 0, "shortcut created: " + Path.GetFileName(path));
                long before = new FileInfo(path).Length;
                string again;
                Check(!Actions.CreateShortcut(a, outDir, out again) && new FileInfo(path).Length == before, "existing shortcut is left unchanged");
            }

            // settings survive a save/load round trip (in the output folder, not the user's real settings)
            Settings.PathOverride = Path.Combine(outDir, "settings.ini");
            if (File.Exists(Settings.PathOverride)) File.Delete(Settings.PathOverride);
            var st = new Settings { Columns = 7, IconSize = 2, Sections = false, Theme = 2, MinimizeToTray = true,
                                    CloseToTray = true, Bounds = new Rectangle(10, 20, 800, 600), Maximized = true };
            st.Save();
            var back = Settings.Load();
            Check(back.Columns == 7 && back.IconSize == 2 && !back.Sections && back.Theme == 2 && back.MinimizeToTray &&
                  back.CloseToTray && back.Bounds == st.Bounds && back.Maximized, "settings survive save and load");
            st.Columns = 4;
            Check(st.Save() && Settings.Load().Columns == 4 && !File.Exists(Settings.PathOverride + ".tmp"), "saving over an existing file replaces it cleanly");
            File.WriteAllText(Settings.PathOverride, "columns=99\niconSize=-4\ntheme=x\n");
            back = Settings.Load();
            Check(back.Columns == Settings.MaxColumns && back.IconSize == 0 && back.Theme == 0, "bad settings values are clamped");
            Settings.Current = new Settings();

            // Start with Windows writes a Run entry that starts quietly in the tray; the original state is restored
            bool hadAutostart = Autostart.Get();
            Autostart.Set(true);
            string runValue;
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                runValue = k.GetValue("AppShelf") as string;
            Check(runValue == "\"" + Application.ExecutablePath + "\" " + Autostart.TrayArg, "Start with Windows entry: " + runValue);
            Autostart.Set(false);
            Check(!Autostart.Get(), "Start with Windows can be turned off");
            Autostart.Set(hadAutostart);

            // render the main window and a details window, in both themes
            AppInfo.Reset();
            var form = new MainForm();
            form.Show();
            var deadline = DateTime.Now.AddSeconds(30);
            while (!form.Loaded && DateTime.Now < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            Check(form.Loaded, "main window finished loading");
            foreach (bool dark in new[] { true, false })
            {
                string theme = dark ? "dark" : "light";
                Theme.Use(dark);
                Pump();
                form.Search("");
                Pump();
                Capture(form, Path.Combine(outDir, "main-" + theme + ".png"));
                form.Search("git");
                Pump();
                Capture(form, Path.Combine(outDir, "search-" + theme + ".png"));
                form.Search("");
                if (desktopApp != null)
                {
                    using (var details = new DetailsForm(desktopApp))
                    {
                        details.Show();
                        Pump();
                        Capture(details, Path.Combine(outDir, "details-" + theme + ".png"));
                        details.Close();
                    }
                }
            }

            // settings window, and the grid with a fixed column count and large icons
            foreach (bool dark in new[] { true, false })
            {
                Theme.Use(dark);
                using (var sf = new SettingsForm(delegate { }, () => form.GridColumns))
                {
                    sf.Show();
                    Pump();
                    Capture(sf, Path.Combine(outDir, "settings-" + (dark ? "dark" : "light") + ".png"));
                    sf.Close();
                }
            }
            Theme.Use(true);
            Settings.Current.Columns = 5;
            Settings.Current.IconSize = 2;
            form.ApplySettings();
            deadline = DateTime.Now.AddSeconds(30);
            Pump();
            while (!form.Loaded && DateTime.Now < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            Pump();
            Check(form.GridColumns == 5, "fixed column count is used (" + form.GridColumns + ")");
            Capture(form, Path.Combine(outDir, "main-5-columns-large.png"));
            Settings.Current.Columns = 0;
            Settings.Current.Sections = false;
            form.ApplySettings();
            Pump();
            Check(form.GridColumns > 0, "auto columns after switching back (" + form.GridColumns + ")");

            Theme.Use(null);
            form.Close();
            Console.WriteLine("\nscreens saved to " + outDir);
            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        static void Pump()
        {
            for (int i = 0; i < 5; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        }

        static void Capture(Form f, string file)
        {
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                bmp.Save(file);
            }
        }
    }
}
#endif

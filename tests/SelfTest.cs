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

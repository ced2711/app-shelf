// Console self-test, compiled only by build.cmd test (defines SELFTEST).
// Usage: AppShelfTest.exe <output folder>

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
        public static void Run(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "appshelf-test");
            Directory.CreateDirectory(outDir);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var apps = Shell.EnumerateApps();
            Console.WriteLine("apps: {0} ({1} ms)", apps.Count, sw.ElapsedMilliseconds);

            sw.Restart();
            int withIcon = 0;
            foreach (var a in apps) { a.Icon = Shell.GetIcon(a.ShellPath, 48); if (a.Icon != null) withIcon++; }
            Console.WriteLine("icons: {0}/{1} ({2} ms)", withIcon, apps.Count, sw.ElapsedMilliseconds);

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

            var desktopApp = apps.FirstOrDefault(a => !a.IsStoreApp && a.HasTargetFile && AppInfo.FindUninstall(a) != null);
            var storeApp = apps.FirstOrDefault(a => a.IsStoreApp);
            var idListApp = apps.FirstOrDefault(a => !a.IsStoreApp && AppInfo.FindStartMenuShortcut(a) == null);
            foreach (var a in new[] { desktopApp, storeApp, idListApp })
            {
                if (a == null) continue;
                Console.WriteLine("\n=== " + a.Name);
                foreach (var kv in AppInfo.GetDetails(a)) Console.WriteLine("  {0,-20} {1}", kv.Key, kv.Value);
                string path;
                bool created = Actions.CreateShortcut(a, outDir, out path);
                Console.WriteLine("  shortcut: {0} created={1} bytes={2}", path, created, File.Exists(path) ? new FileInfo(path).Length : -1);
            }

            // render the main window and a details window
            var form = new MainForm();
            form.Show();
            var deadline = DateTime.Now.AddSeconds(30);
            while (!form.Loaded && DateTime.Now < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            Application.DoEvents();
            Capture(form, Path.Combine(outDir, "main.png"));
            if (desktopApp != null)
            {
                var details = new DetailsForm(desktopApp);
                details.Show();
                Application.DoEvents();
                Capture(details, Path.Combine(outDir, "details.png"));
                details.Close();
            }
            form.Close();
            Console.WriteLine("\nscreens saved to " + outDir);
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

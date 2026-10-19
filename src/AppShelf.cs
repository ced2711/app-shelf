// AppShelf - a tiny viewer for every app in the Windows "All apps" list (shell:AppsFolder).
// Build with build.cmd (uses the C# compiler that ships with Windows / .NET Framework 4.x).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppShelf
{
    // ------------------------------------------------------------------ model

    class AppEntry
    {
        public string Name;
        public string Id;                 // parsing name inside shell:AppsFolder (AUMID or known-folder path)
        public string TargetPath;         // System.Link.TargetParsingPath (desktop apps)
        public string Arguments;
        public string PackageFullName;
        public string PackageFamilyName;
        public string PackageInstallPath;
        public Bitmap Icon;

        public bool IsStoreApp { get { return !string.IsNullOrEmpty(PackageFullName); } }
        public string ShellPath { get { return "shell:AppsFolder\\" + Id; } }
        public bool HasTargetFile { get { return !string.IsNullOrEmpty(TargetPath) && File.Exists(TargetPath); } }
    }

    class Detail
    {
        public string Section, Key, Value;
    }

    class UninstallInfo
    {
        public string DisplayName, Publisher, DisplayVersion, InstallLocation, InstallDate,
                      UninstallString, DisplayIcon, KeyPath;
        public long SizeKB;
    }

    // ------------------------------------------------------------------ shell interop

    enum SIGDN : uint
    {
        NORMALDISPLAY = 0,
        PARENTRELATIVEPARSING = 0x80018001,
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem2
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyStoreWithCreateObject(int flags, IntPtr punkCreateObject, ref Guid riid, out IntPtr ppv);
        void GetPropertyStoreForKeys(IntPtr rgKeys, uint cKeys, int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);
        void Update(IntPtr pbc);
        void GetProperty(ref PROPERTYKEY key, IntPtr ppropvar);
        void GetCLSID(ref PROPERTYKEY key, out Guid pclsid);
        void GetFileTime(ref PROPERTYKEY key, out long pft);
        void GetInt32(ref PROPERTYKEY key, out int pi);
        [PreserveSig] int GetString(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class CShellLink { }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor, hProcess;
    }

    static class Native
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("shell32.dll")]
        public static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, out IntPtr ppidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        [DllImport("propsys.dll", CharSet = CharSet.Unicode)]
        public static extern int PSGetPropertyKeyFromName(string pszName, out PROPERTYKEY ppropkey);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public const int EM_SETCUEBANNER = 0x1501;
    }

    static class Shell
    {
        static readonly Guid BHID_EnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
        static readonly Guid IID_IEnumShellItems = new Guid("70629033-e363-4a28-a567-0db78006e6d7");
        static readonly Guid IID_IShellItem2 = new Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93");
        const int SIIGBF_ICONONLY = 0x4;

        static readonly Dictionary<string, PROPERTYKEY> keys = new Dictionary<string, PROPERTYKEY>();

        public static IShellItem2 FromParsingName(string path)
        {
            Guid iid = IID_IShellItem2;
            object o;
            if (Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out o) != 0) return null;
            return o as IShellItem2;
        }

        static string GetName(IShellItem item, SIGDN kind)
        {
            try { string s; item.GetDisplayName(kind, out s); return s; }
            catch { return null; }
        }

        public static string GetProp(IShellItem2 item, string canonicalName)
        {
            PROPERTYKEY key;
            lock (keys)
            {
                if (!keys.TryGetValue(canonicalName, out key))
                {
                    if (Native.PSGetPropertyKeyFromName(canonicalName, out key) != 0) return null;
                    keys[canonicalName] = key;
                }
            }
            string value;
            if (item.GetString(ref key, out value) != 0) return null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // Enumerates the same list as Start > All apps (desktop and Store apps).
        public static List<AppEntry> EnumerateApps()
        {
            var result = new List<AppEntry>();
            var folder = FromParsingName("shell:AppsFolder");
            if (folder == null) return result;
            Guid bhid = BHID_EnumItems, iid = IID_IEnumShellItems;
            object eo;
            folder.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out eo);
            var en = (IEnumShellItems)eo;
            IShellItem item;
            uint fetched;
            while (en.Next(1, out item, out fetched) == 0 && fetched == 1)
            {
                var e = new AppEntry();
                e.Name = GetName(item, SIGDN.NORMALDISPLAY);
                e.Id = GetName(item, SIGDN.PARENTRELATIVEPARSING);
                var item2 = item as IShellItem2;
                if (item2 != null)
                {
                    e.TargetPath = GetProp(item2, "System.Link.TargetParsingPath");
                    e.Arguments = GetProp(item2, "System.Link.Arguments");
                    e.PackageFullName = GetProp(item2, "System.AppUserModel.PackageFullName");
                    e.PackageFamilyName = GetProp(item2, "System.AppUserModel.PackageFamilyName");
                    e.PackageInstallPath = GetProp(item2, "System.AppUserModel.PackageInstallPath");
                }
                if (!string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.Id)) result.Add(e);
                Marshal.ReleaseComObject(item);
            }
            Marshal.ReleaseComObject(en);
            Marshal.ReleaseComObject(folder);
            return result;
        }

        public static Bitmap GetIcon(string shellPath, int size)
        {
            var item = FromParsingName(shellPath);
            if (item == null) return null;
            try
            {
                var factory = item as IShellItemImageFactory;
                if (factory == null) return null;
                IntPtr hbm;
                if (factory.GetImage(new SIZE(size, size), SIIGBF_ICONONLY, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                try { return BitmapFromHBitmap(hbm); }
                finally { Native.DeleteObject(hbm); }
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(item); }
        }

        // Image.FromHbitmap drops the alpha channel; reinterpret the 32-bit pixels to keep transparency.
        static Bitmap BitmapFromHBitmap(IntPtr hbm)
        {
            using (var bmp = Image.FromHbitmap(hbm))
            {
                if (Image.GetPixelFormatSize(bmp.PixelFormat) < 32) return new Bitmap(bmp);
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, bmp.PixelFormat);
                try
                {
                    // Stride is negative for bottom-up bitmaps, so copy row by row.
                    int rowBytes = Math.Abs(data.Stride);
                    var bytes = new byte[rowBytes * data.Height];
                    for (int y = 0; y < data.Height; y++)
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), bytes, y * rowBytes, rowBytes);
                    bool hasAlpha = false;
                    for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] != 0) { hasAlpha = true; break; }
                    var format = hasAlpha ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppRgb;
                    using (var view = new Bitmap(data.Width, data.Height, data.Stride, format, data.Scan0))
                        return new Bitmap(view);
                }
                finally { bmp.UnlockBits(data); }
            }
        }

        public static IntPtr GetPidl(string shellPath)
        {
            var item = FromParsingName(shellPath);
            if (item == null) return IntPtr.Zero;
            try
            {
                IntPtr pidl;
                return Native.SHGetIDListFromObject(item, out pidl) == 0 ? pidl : IntPtr.Zero;
            }
            finally { Marshal.ReleaseComObject(item); }
        }

        public static IShellItem2 GetItem(AppEntry app) { return FromParsingName(app.ShellPath); }
    }

    // ------------------------------------------------------------------ details & actions

    static class AppInfo
    {
        static List<UninstallInfo> uninstall;
        static Dictionary<string, string> startMenu;

        // Forget cached registry and Start menu data so a refresh picks up newly installed apps.
        public static void Reset()
        {
            uninstall = null;
            startMenu = null;
        }

        static List<UninstallInfo> Uninstall
        {
            get
            {
                if (uninstall == null) uninstall = LoadUninstall();
                return uninstall;
            }
        }

        static List<UninstallInfo> LoadUninstall()
        {
            var list = new List<UninstallInfo>();
            const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            var roots = new[]
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Default),
            };
            foreach (var r in roots)
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(r.Item1, r.Item2))
                    using (var key = baseKey.OpenSubKey(sub))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetSubKeyNames())
                        {
                            using (var k = key.OpenSubKey(name))
                            {
                                if (k == null) continue;
                                var u = new UninstallInfo();
                                u.DisplayName = k.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(u.DisplayName)) continue;
                                u.Publisher = k.GetValue("Publisher") as string;
                                u.DisplayVersion = k.GetValue("DisplayVersion") as string;
                                u.InstallLocation = k.GetValue("InstallLocation") as string;
                                u.InstallDate = k.GetValue("InstallDate") as string;
                                u.UninstallString = k.GetValue("UninstallString") as string;
                                u.DisplayIcon = k.GetValue("DisplayIcon") as string;
                                object size = k.GetValue("EstimatedSize");
                                if (size is int) u.SizeKB = (int)size;
                                u.KeyPath = k.Name;
                                list.Add(u);
                            }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            p = p.Trim().Trim('"');
            int comma = p.LastIndexOf(',');
            if (comma > 2 && !p.Substring(comma + 1).Contains("\\")) p = p.Substring(0, comma).Trim().Trim('"');
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(p)).TrimEnd('\\').ToLowerInvariant(); }
            catch { return null; }
        }

        static HashSet<string> genericFolders;

        static bool IsGenericFolder(string normalized)
        {
            if (genericFolders == null)
            {
                genericFolders = new HashSet<string>();
                var folders = new[]
                {
                    Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
                    Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                    Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
                    Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                };
                foreach (var f in folders)
                {
                    var p = NormalizePath(Environment.GetFolderPath(f));
                    if (p != null) genericFolders.Add(p);
                }
                var programs = NormalizePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
                if (programs != null) genericFolders.Add(programs);
            }
            return normalized.Length <= 3 || genericFolders.Contains(normalized);
        }

        // Best-effort match between an app and its "Installed apps" (Uninstall) registry entry.
        public static UninstallInfo FindUninstall(AppEntry app)
        {
            string target = NormalizePath(app.TargetPath);
            UninstallInfo best = null;
            int bestScore = 0;
            foreach (var u in Uninstall)
            {
                int score = 0;
                if (target != null)
                {
                    if (NormalizePath(u.DisplayIcon) == target) score = 3000;
                    var loc = NormalizePath(u.InstallLocation);
                    if (score == 0 && loc != null && !IsGenericFolder(loc) && target.StartsWith(loc + "\\")) score = 1000 + loc.Length;
                }
                if (score == 0 && string.Equals(u.DisplayName, app.Name, StringComparison.OrdinalIgnoreCase)) score = 500;
                if (score > bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        // Start menu shortcut whose file name matches the app name (used to copy the original shortcut).
        public static string FindStartMenuShortcut(AppEntry app)
        {
            if (startMenu == null)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                                            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                        {
                            var name = Path.GetFileNameWithoutExtension(f);
                            if (!map.ContainsKey(name)) map[name] = f;
                        }
                    }
                    catch { }
                }
                startMenu = map;
            }
            string path;
            return startMenu.TryGetValue(app.Name, out path) ? path : null;
        }

        static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString("0.#")) + " " + units[i];
        }

        static string FormatInstallDate(string d)
        {
            DateTime t;
            if (d != null && DateTime.TryParseExact(d, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out t))
                return t.ToString("yyyy-MM-dd");
            return d;
        }

        public static List<Detail> GetDetails(AppEntry app)
        {
            var rows = new List<Detail>();
            string section = "App";
            Action<string, string> add = (k, v) => { if (!string.IsNullOrWhiteSpace(v)) rows.Add(new Detail { Section = section, Key = k, Value = v.Trim() }); };

            add("Name", app.Name);
            add("Type", app.IsStoreApp ? "Store app (MSIX / UWP)" : "Desktop app");
            add("App ID", app.Id);

            if (app.IsStoreApp)
            {
                var parts = app.PackageFullName.Split('_');
                section = "Package";
                add("Package", app.PackageFamilyName);
                add("Package full name", app.PackageFullName);
                if (parts.Length > 1) add("Version", parts[1]);
                if (parts.Length > 2) add("Architecture", parts[2]);
                add("Install location", app.PackageInstallPath);
                try
                {
                    var manifest = Path.Combine(app.PackageInstallPath ?? "", "AppxManifest.xml");
                    if (File.Exists(manifest))
                    {
                        var m = Regex.Match(File.ReadAllText(manifest), "<PublisherDisplayName>(.*?)</PublisherDisplayName>");
                        if (m.Success && !m.Groups[1].Value.StartsWith("ms-resource:")) add("Publisher", m.Groups[1].Value);
                    }
                }
                catch { }
                return rows;
            }

            add("Target", app.TargetPath);
            add("Arguments", app.Arguments);
            add("Start menu shortcut", FindStartMenuShortcut(app));

            if (app.HasTargetFile)
            {
                section = "File";
                try
                {
                    var fv = FileVersionInfo.GetVersionInfo(app.TargetPath);
                    add("Description", fv.FileDescription);
                    add("Company", fv.CompanyName);
                    add("Product", fv.ProductName);
                    add("File version", fv.FileVersion);
                    if (fv.ProductVersion != fv.FileVersion) add("Product version", fv.ProductVersion);
                    add("Copyright", fv.LegalCopyright);
                }
                catch { }
                try
                {
                    var fi = new FileInfo(app.TargetPath);
                    add("File size", FormatSize(fi.Length));
                    add("File created", fi.CreationTime.ToString("yyyy-MM-dd HH:mm"));
                    add("File modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                }
                catch { }
            }

            var u = FindUninstall(app);
            if (u != null)
            {
                section = "Installation";
                add("Installed as", u.DisplayName);
                add("Publisher", u.Publisher);
                add("Installed version", u.DisplayVersion);
                add("Install date", FormatInstallDate(u.InstallDate));
                add("Install location", u.InstallLocation);
                if (u.SizeKB > 0) add("Size (estimated)", FormatSize(u.SizeKB * 1024L));
                add("Uninstall command", u.UninstallString);
                add("Registry key", u.KeyPath);
            }
            return rows;
        }
    }

    static class Actions
    {
        public static void Launch(AppEntry app, IWin32Window owner)
        {
            IntPtr pidl = Shell.GetPidl(app.ShellPath);
            try
            {
                if (pidl != IntPtr.Zero)
                {
                    var sei = new SHELLEXECUTEINFO();
                    sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                    sei.fMask = 0x0000000C; // SEE_MASK_INVOKEIDLIST
                    sei.lpIDList = pidl;
                    sei.nShow = 1;          // SW_SHOWNORMAL
                    if (Native.ShellExecuteEx(ref sei)) return;
                }
                Process.Start("explorer.exe", app.ShellPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Could not open " + app.Name + ":\n" + ex.Message, "AppShelf",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        public static string GetLocation(AppEntry app)
        {
            if (app.HasTargetFile) return app.TargetPath;
            if (!string.IsNullOrEmpty(app.PackageInstallPath) && Directory.Exists(app.PackageInstallPath)) return app.PackageInstallPath;
            return AppInfo.FindStartMenuShortcut(app);
        }

        public static void OpenLocation(AppEntry app)
        {
            var loc = GetLocation(app);
            if (loc == null) return;
            if (File.Exists(loc)) Process.Start("explorer.exe", "/select,\"" + loc + "\"");
            else Process.Start("explorer.exe", "\"" + loc + "\"");
        }

        static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }

        // Creates "<dir>\<name>.lnk". Returns false if a shortcut with that name already exists.
        public static bool CreateShortcut(AppEntry app, string dir, out string path)
        {
            path = Path.Combine(dir, SafeFileName(app.Name) + ".lnk");
            if (File.Exists(path)) return false;

            // Desktop apps: copy the original Start menu shortcut (keeps arguments, icon and working folder).
            var source = app.IsStoreApp ? null : AppInfo.FindStartMenuShortcut(app);
            if (source != null)
            {
                File.Copy(source, path);
                return true;
            }

            // Otherwise link to the item in shell:AppsFolder, like dragging it from Start to the desktop.
            IntPtr pidl = Shell.GetPidl(app.ShellPath);
            if (pidl == IntPtr.Zero) throw new InvalidOperationException("The app could not be resolved.");
            try
            {
                var link = (IShellLinkW)new CShellLink();
                link.SetIDList(pidl);
                ((IPersistFile)link).Save(path, true);
                Marshal.ReleaseComObject(link);
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
            return true;
        }

        static string Quote(string name) { return "\u201C" + name + "\u201D"; }

        // Adds desktop shortcuts and returns a one-line summary for a toast. Failures also get a dialog with the reasons.
        public static string AddToDesktop(IList<AppEntry> apps, IWin32Window owner, out bool warn)
        {
            warn = false;
            if (apps.Count == 0) return null;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var added = new List<string>();
            var existing = new List<string>();
            var failed = new List<string>();
            foreach (var app in apps)
            {
                try
                {
                    string path;
                    if (CreateShortcut(app, desktop, out path)) added.Add(app.Name);
                    else existing.Add(app.Name);
                }
                catch (Exception ex) { failed.Add(app.Name + " (" + ex.Message + ")"); }
            }

            if (failed.Count > 0)
            {
                warn = true;
                MessageBox.Show(owner, "Could not add to the desktop:\n  " + string.Join("\n  ", failed), "AppShelf",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            var parts = new List<string>();
            if (added.Count == 1) parts.Add("Added " + Quote(added[0]) + " to the desktop");
            else if (added.Count > 1) parts.Add("Added " + added.Count + " shortcuts to the desktop");
            if (existing.Count == 1) parts.Add(Quote(existing[0]) + " is already on the desktop");
            else if (existing.Count > 1) parts.Add(existing.Count + " were already on the desktop");
            if (failed.Count > 0) parts.Add(failed.Count + " failed");
            return string.Join("  \u00B7  ", parts);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = g.DpiX / 96f;
            if (args.Length == 2 && args[0] == "--write-icon") { Logo.WriteIco(args[1]); return; }
#if SELFTEST
            SelfTest.Run(args);
#else
            Application.Run(new MainForm());
#endif
        }
    }
}

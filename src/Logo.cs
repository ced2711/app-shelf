// The AppShelf logo: four rounded app tiles on a dark plate, one lit in the accent color.
// Drawn in code so the exe icon, window icon and header all come from the same source.

using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace AppShelf
{
    static class Logo
    {
        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size;
                Theme.FillRound(g, Color.FromArgb(30, 31, 38), new RectangleF(0, 0, s, s), s * 0.24f);

                float m = s * 0.2f, gap = s * 0.08f, t = (s - 2 * m - gap) / 2, r = t * 0.3f;
                if (size <= 20) { m = s * 0.16f; gap = s * 0.1f; t = (s - 2 * m - gap) / 2; r = t * 0.25f; }
                var tiles = new[]
                {
                    new { X = m, Y = m, C = Color.FromArgb(122, 162, 255) },
                    new { X = m + t + gap, Y = m, C = Color.FromArgb(206, 210, 222) },
                    new { X = m, Y = m + t + gap, C = Color.FromArgb(206, 210, 222) },
                    new { X = m + t + gap, Y = m + t + gap, C = Color.FromArgb(108, 114, 134) },
                };
                foreach (var tile in tiles)
                    Theme.FillRound(g, tile.C, new RectangleF(tile.X, tile.Y, t, t), r);
            }
            return bmp;
        }

        public static Icon MakeIcon(int size)
        {
            using (var bmp = Render(size))
                return Icon.FromHandle(bmp.GetHicon());
        }

        // Writes a multi-resolution .ico made of PNG frames (used by build.cmd for the exe icon).
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Render(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    frames.Add(ms.ToArray());
                }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim);
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
        }
    }
}

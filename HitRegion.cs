using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskMadeline
{
    /// <summary>
    /// Cuts an input window down to the shape of what is drawn in it.
    /// </summary>
    /// <remarks>
    /// The input windows exist to be clicked and dragged and are never meant to be seen, but
    /// Windows hit-tests straight through a layered window at alpha 0, so they cannot be
    /// nothing at all: at their thinnest they still darken what is behind them by one level.
    /// A rectangle of that is faint but perfectly visible against a flat colour -- and the
    /// desktop is mostly flat colours.
    ///
    /// So the window is shaped like the sprite instead of like its bounding box. What is left
    /// of it lies under pixels the pet has drawn over anyway, and nothing of it lies anywhere
    /// else, which is the only way to be both invisible and clickable at once. Where she is
    /// drawn is also the right place to be able to grab her.
    /// </remarks>
    internal static class HitRegion
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RegionDataHeader
        {
            public int Size, Type, Count, RgnSize;
            public int Left, Top, Right, Bottom;
        }

        const int RdhRectangles = 1;

        /// <summary>
        /// Shape <paramref name="window"/> like the opaque pixels of <paramref name="source"/>
        /// inside <paramref name="area"/>, each source pixel standing for a scale x scale block.
        /// </summary>
        /// <param name="mask">
        /// The last shape applied, kept by the caller. A sprite holds still for frames at a
        /// time and the region is the same each time, so this is what stops the window being
        /// reshaped sixty times a second for nothing.
        /// </param>
        public static void Apply(IntPtr window, Bitmap source, Rectangle area, int scale,
            ref byte[] mask)
        {
            if (window == IntPtr.Zero || source == null || area.Width <= 0 || area.Height <= 0)
                return;
            area = Rectangle.Intersect(area, new Rectangle(0, 0, source.Width, source.Height));
            if (area.Width <= 0 || area.Height <= 0) return;

            byte[] opaque = ReadAlpha(source, area);
            if (opaque == null) return;
            if (mask != null && mask.Length == opaque.Length)
            {
                bool same = true;
                for (int i = 0; i < opaque.Length && same; i++) same = mask[i] == opaque[i];
                if (same) return;
            }
            mask = opaque;

            IntPtr region = Build(opaque, area.Width, area.Height, scale);
            if (region == IntPtr.Zero) return;
            // The window takes the region over; a zero return means it did not, so free it.
            if (Win32.SetWindowRgn(window, region, false) == 0) Win32.DeleteObject(region);
        }

        /// <summary>One byte per pixel: whether anything was drawn there.</summary>
        static byte[] ReadAlpha(Bitmap source, Rectangle area)
        {
            BitmapData data = null;
            try
            {
                data = source.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                var opaque = new byte[area.Width * area.Height];
                for (int y = 0; y < area.Height; y++)
                {
                    IntPtr row = data.Scan0 + y * data.Stride;
                    for (int x = 0; x < area.Width; x++)
                        opaque[y * area.Width + x] =
                            Marshal.ReadByte(row, x * 4 + 3) == 0 ? (byte)0 : (byte)1;
                }
                return opaque;
            }
            catch { return null; }
            finally { if (data != null) source.UnlockBits(data); }
        }

        /// <summary>The opaque pixels as one region, a rectangle per run of them.</summary>
        static IntPtr Build(byte[] opaque, int width, int height, int scale)
        {
            var rectangles = new MemoryStream();
            var writer = new BinaryWriter(rectangles);
            int count = 0;
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    while (x < width && opaque[y * width + x] == 0) x++;
                    if (x >= width) break;
                    int start = x;
                    while (x < width && opaque[y * width + x] != 0) x++;
                    int l = start * scale, t = y * scale, r = x * scale, b = (y + 1) * scale;
                    writer.Write(l); writer.Write(t); writer.Write(r); writer.Write(b);
                    count++;
                    if (l < left) left = l;
                    if (t < top) top = t;
                    if (r > right) right = r;
                    if (b > bottom) bottom = b;
                }
            }
            // Nothing drawn: an empty region, which is a window that cannot be clicked at all.
            // That is right -- there is nothing there to click.
            if (count == 0) { left = top = right = bottom = 0; }

            var header = new RegionDataHeader
            {
                Size = Marshal.SizeOf<RegionDataHeader>(),
                Type = RdhRectangles,
                Count = count,
                RgnSize = count * 16,
                Left = left, Top = top, Right = right, Bottom = bottom
            };
            byte[] blob = new byte[header.Size + header.RgnSize];
            IntPtr scratch = Marshal.AllocHGlobal(header.Size);
            try
            {
                Marshal.StructureToPtr(header, scratch, false);
                Marshal.Copy(scratch, blob, 0, header.Size);
            }
            finally { Marshal.FreeHGlobal(scratch); }
            Buffer.BlockCopy(rectangles.GetBuffer(), 0, blob, header.Size, header.RgnSize);
            return Win32.ExtCreateRegion(IntPtr.Zero, (uint)blob.Length, blob);
        }
    }
}

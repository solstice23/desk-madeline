using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DeskMadeline
{
    /// <summary>
    /// The 1x game-pixel buffer a frame is drawn into, and which part of it holds anything.
    /// </summary>
    /// <remarks>
    /// The buffer is wide on purpose -- her particles fly a long way at speed, entities are
    /// drawn wherever they are, and the camera never chases either -- but on almost every
    /// frame what is drawn is a patch around her a few dozen pixels across. Clearing,
    /// uploading and drawing the whole square every frame was most of what a frame cost,
    /// spent on pixels that were transparent before and after.
    ///
    /// So after each frame the buffer is measured: <see cref="Content"/> is the smallest
    /// rectangle outside which every pixel is exactly zero, wherever on the canvas that is.
    /// Clearing only that rectangle next frame leaves the whole buffer zero, bit for bit what
    /// clearing all of it did, and a copy of the buffer elsewhere stays exact by being sent
    /// only <see cref="Measure"/>'s answer: every pixel that was drawn then or is drawn now.
    ///
    /// Measuring does not read the whole square either. The pixels live in memory Windows
    /// watches for writes (MEM_WRITE_WATCH), which reports every page written since the last
    /// clear; a row nothing wrote to is still the zero it was cleared to, so only the rows
    /// on written pages are looked at. Should the watch ever fail, every row is.
    /// </remarks>
    sealed unsafe class GameCanvas
    {
        public readonly int Width, Height;
        /// <summary>The canvas as GDI+ sees it, drawing straight into <see cref="pixels"/>.</summary>
        public readonly Bitmap Bitmap;
        readonly uint* pixels;
        readonly nuint bytes;
        readonly bool watched;
        readonly IntPtr[] written;
        readonly bool[] rowWritten;
        Rectangle content;

        public GameCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            bytes = (nuint)width * (nuint)height * 4;
            // Committed memory comes zeroed: an empty canvas, as a cleared one is.
            IntPtr memory = VirtualAlloc(IntPtr.Zero, bytes, MEM_RESERVE | MEM_COMMIT | MEM_WRITE_WATCH,
                PAGE_READWRITE);
            watched = memory != IntPtr.Zero;
            if (!watched)
                memory = VirtualAlloc(IntPtr.Zero, bytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            if (memory == IntPtr.Zero) throw new OutOfMemoryException("canvas");
            pixels = (uint*)memory;
            written = new IntPtr[(int)(bytes / 4096) + 1];
            rowWritten = new bool[height];
            Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, memory);
        }

        /// <summary>Where anything was drawn by the last measured frame; empty for nothing.</summary>
        public Rectangle Content => content;

        Span<uint> Row(int y) => new Span<uint>(pixels + (nint)y * Width, Width);

        /// <summary>
        /// Every pixel back to zero, as Graphics.Clear(Color.Transparent) leaves them: in
        /// premultiplied ARGB, transparent is all four channels zero.
        /// </summary>
        public void Clear()
        {
            for (int y = content.Top; y < content.Bottom; y++)
                Row(y).Slice(content.Left, content.Width).Clear();
            // What clearing wrote is not drawing; the watch starts again from here.
            if (watched) ResetWriteWatch((IntPtr)pixels, bytes);
        }

        /// <summary>
        /// Measure what the frame just drawn covers, and say which pixels differ from the
        /// last measured frame: at most those inside either frame's content.
        /// </summary>
        public Rectangle Measure()
        {
            bool everyRow = !MarkWrittenRows();
            int top = -1, bottom = -1, left = Width, right = 0;
            for (int y = 0; y < Height; y++)
            {
                if (!everyRow && !rowWritten[y]) continue;
                ReadOnlySpan<uint> row = Row(y);
                int first = row.IndexOfAnyExcept(0u);
                if (first < 0) continue;
                if (top < 0) top = y;
                bottom = y;
                if (first < left) left = first;
                int last = row.LastIndexOfAnyExcept(0u);
                if (last + 1 > right) right = last + 1;
            }
            Rectangle measured = top < 0 ? Rectangle.Empty
                : Rectangle.FromLTRB(left, top, right, bottom + 1);
            Rectangle changed = Union(content, measured);
            content = measured;
            return changed;
        }

        /// <summary>Which rows lie on pages written since <see cref="Clear"/>; false if unknown.</summary>
        bool MarkWrittenRows()
        {
            if (!watched) return false;
            Array.Clear(rowWritten);
            nuint count = (nuint)written.Length;
            if (GetWriteWatch(0, (IntPtr)pixels, bytes, written, ref count, out uint pageSize) != 0 ||
                count >= (nuint)written.Length)
                return false;
            int stride = Width * 4;
            for (int i = 0; i < (int)count; i++)
            {
                long offset = (long)written[i] - (long)pixels;
                int first = (int)(offset / stride);
                int last = (int)Math.Min(Height - 1, (offset + pageSize - 1) / stride);
                for (int y = Math.Max(0, first); y <= last; y++) rowWritten[y] = true;
            }
            return true;
        }

        /// <summary>Copy a rectangle of the canvas out as tightly packed rows.</summary>
        public void CopyTo(Rectangle area, byte[] destination)
        {
            Span<byte> into = destination;
            int rowBytes = area.Width * 4;
            for (int y = 0; y < area.Height; y++)
                MemoryMarshal.AsBytes(Row(area.Top + y).Slice(area.Left, area.Width))
                    .CopyTo(into.Slice(y * rowBytes, rowBytes));
        }

        // Rectangle.Union treats an empty rectangle as a point at the origin.
        static Rectangle Union(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            return Rectangle.Union(a, b);
        }

        const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_WRITE_WATCH = 0x200000;
        const uint PAGE_READWRITE = 0x04;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protect);

        [DllImport("kernel32.dll")]
        static extern uint GetWriteWatch(uint flags, IntPtr baseAddress, nuint regionSize,
            [Out] IntPtr[] addresses, ref nuint count, out uint granularity);

        [DllImport("kernel32.dll")]
        static extern uint ResetWriteWatch(IntPtr baseAddress, nuint regionSize);
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DeskMadeline
{
    /// <summary>
    /// Reads sprites out of an installed Celeste's atlases, the way SoundEffects reads its
    /// banks, so a build need not carry any of the game's artwork.
    /// </summary>
    /// <remarks>
    /// Both formats are Monocle's and are ported from it. Atlas.ReadAtlasData's Packer case
    /// gives the .meta layout, and VirtualTexture's ".data" case the image: a width, a height,
    /// a flag for whether alpha is stored, then runs of pixels. Each run is a length byte, an
    /// alpha byte when alpha is stored, and the colour as B, G, R -- which is already the byte
    /// order GDI+ wants, so the colour copies straight across.
    /// </remarks>
    internal static class CelesteAtlas
    {
        /// <summary>One sprite's place on a page, and where it sits in its untrimmed frame.</summary>
        /// <remarks>
        /// OffsetX and OffsetY are where the trimmed region belongs within the frame, which is
        /// the negative of what the file stores -- Monocle negates them the same way.
        /// </remarks>
        internal readonly struct Entry
        {
            public readonly int Page, X, Y, Width, Height, OffsetX, OffsetY, FrameWidth, FrameHeight;

            public Entry(int page, int x, int y, int width, int height,
                int offsetX, int offsetY, int frameWidth, int frameHeight)
            {
                Page = page; X = x; Y = y; Width = width; Height = height;
                OffsetX = offsetX; OffsetY = offsetY; FrameWidth = frameWidth; FrameHeight = frameHeight;
            }
        }

        /// <summary>Every sprite in an atlas, by its path, plus the pages they live on.</summary>
        internal static Dictionary<string, Entry> ReadMeta(string metaPath, out List<string> pages)
        {
            var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            pages = new List<string>();

            using var stream = File.OpenRead(metaPath);
            using var reader = new BinaryReader(stream);
            reader.ReadInt32();     // version
            reader.ReadString();    // packer
            reader.ReadInt32();     // packer version
            short pageCount = reader.ReadInt16();
            for (int page = 0; page < pageCount; page++)
            {
                pages.Add(reader.ReadString());
                short spriteCount = reader.ReadInt16();
                for (int i = 0; i < spriteCount; i++)
                {
                    string path = reader.ReadString().Replace('\\', '/');
                    short x = reader.ReadInt16(), y = reader.ReadInt16();
                    short width = reader.ReadInt16(), height = reader.ReadInt16();
                    short offsetX = reader.ReadInt16(), offsetY = reader.ReadInt16();
                    short frameWidth = reader.ReadInt16(), frameHeight = reader.ReadInt16();
                    entries[path] = new Entry(page, x, y, width, height,
                        -offsetX, -offsetY, frameWidth, frameHeight);
                }
            }
            return entries;
        }

        /// <summary>Decode one page. These are large, so read what is needed and let it go.</summary>
        internal static Bitmap DecodePage(string dataPath)
        {
            using var stream = File.OpenRead(dataPath);
            using var reader = new BinaryReader(stream);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            bool hasAlpha = reader.ReadByte() == 1;

            byte[] pixels = new byte[width * height * 4];
            byte[] run = new byte[4];
            int at = 0;
            while (at < pixels.Length)
            {
                int length = reader.ReadByte();
                byte alpha = hasAlpha ? reader.ReadByte() : (byte)255;
                if (alpha > 0)
                {
                    // B, G, R in the file, and B, G, R, A in a GDI+ 32bpp bitmap.
                    reader.Read(run, 0, 3);
                    run[3] = alpha;
                    for (int i = 0; i < length && at < pixels.Length; i++, at += 4)
                        Buffer.BlockCopy(run, 0, pixels, at, 4);
                }
                else
                {
                    // A transparent run stores no colour at all.
                    at += Math.Min(length * 4, pixels.Length - at);
                }
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < height; row++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, row * width * 4,
                        locked.Scan0 + row * locked.Stride, width * 4);
            }
            finally { bitmap.UnlockBits(locked); }
            return bitmap;
        }

        /// <summary>Cut a sprite out of its page and give it its untrimmed frame back.</summary>
        internal static Bitmap Extract(Bitmap page, in Entry entry)
        {
            int width = Math.Max(1, entry.FrameWidth), height = Math.Max(1, entry.FrameHeight);
            var sprite = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(sprite);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            // The packer trims transparent edges away and records where what is left belongs.
            g.DrawImage(page,
                new Rectangle(entry.OffsetX, entry.OffsetY, entry.Width, entry.Height),
                new Rectangle(entry.X, entry.Y, entry.Width, entry.Height),
                GraphicsUnit.Pixel);
            return sprite;
        }
    }
}

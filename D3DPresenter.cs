using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DirectComposition.DComp;

namespace DeskMadeline
{
    readonly struct TrailStamp
    {
        public readonly Bitmap Bitmap;
        public readonly float X, Y, Opacity;

        public TrailStamp(Bitmap bitmap, float x, float y, float opacity)
        {
            Bitmap = bitmap;
            X = x;
            Y = y;
            Opacity = opacity;
        }
    }

    /// <summary>
    /// Uploads 1x premultiplied game layers to Direct2D and draws them at absolute
    /// physical coordinates in a virtual-desktop-sized Direct3D 11 composition swap
    /// chain. Neither the owning HWND nor its visual tree moves between frames.
    /// </summary>
    sealed class D3DPresenter : IDisposable
    {
        readonly IntPtr hwnd;
        readonly int sourceWidth;
        readonly int sourceHeight;
        readonly Rectangle virtualDesktop;

        ID3D11Device d3dDevice;
        ID3D11DeviceContext d3dContext;
        IDXGIDevice dxgiDevice;
        IDXGIAdapter adapter;
        IDXGIFactory2 dxgiFactory;
        ID2D1Factory1 d2dFactory;
        ID2D1Device d2dDevice;
        ID2D1DeviceContext d2dContext;
        IDCompositionDevice compositionDevice;
        IDCompositionTarget compositionTarget;
        IDCompositionVisual compositionVisual;
        IDXGISwapChain1 swapChain;
        ID2D1Bitmap1 targetBitmap;
        ID2D1Bitmap1 sourceBitmap;
        // A new sourceBitmap's pixels are undefined until something is copied into all of it;
        // after that it only ever needs the part of the canvas that changed.
        bool sourceUndefined;
        byte[] uploadScratch = Array.Empty<byte>();

        // What the swap chain's buffers hold. A flip-model buffer keeps what was drawn into it
        // the last time it was the back buffer, and the only non-transparent pixels in it are
        // the ones drawn then. So instead of clearing the whole desktop-sized target, clearing
        // what the last few frames drew leaves a buffer bit for bit as a full clear would.
        // BufferCount frames back is where this buffer was last drawn; one more is kept in
        // case a present ever does not rotate them. A new swap chain's buffers hold nothing
        // known, so its first frames are cleared whole.
        const int BufferCount = 2;
        readonly Rectangle[] drawnBefore = new Rectangle[BufferCount + 1];
        int wholeClears;
        Rectangle drawnNow;
        // And what the compositor is told. Without it a present means the whole desktop-sized
        // surface may have changed, and DWM composes all of it again every frame -- which
        // cost it more GPU than everything else on the screen together. The pixels that can
        // differ from the frame on screen are the ones drawn then and the ones drawn now;
        // everywhere else both frames are transparent, which is the promise a dirty
        // rectangle makes.
        readonly Vortice.RawRect[] dirtyRectangle = new Vortice.RawRect[1];
        readonly Dictionary<Bitmap, ID2D1Bitmap1> trailBitmaps = new Dictionary<Bitmap, ID2D1Bitmap1>();
        readonly HashSet<Bitmap> liveTrailBitmaps = new HashSet<Bitmap>();
        readonly List<Bitmap> deadTrailBitmaps = new List<Bitmap>();

        int scale;
        int targetWidth;
        int targetHeight;
        bool logged;

        public D3DPresenter(IntPtr hwnd, int sourceWidth, int sourceHeight, int scale, Rectangle virtualDesktop)
        {
            this.hwnd = hwnd;
            this.sourceWidth = sourceWidth;
            this.sourceHeight = sourceHeight;
            this.virtualDesktop = virtualDesktop;
            CreateDevices();
            CreateCompositionTree();
            Resize(scale);
        }

        void CreateDevices()
        {
            Vortice.Direct3D.FeatureLevel[] levels =
            {
                Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0,
                Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0
            };
            try
            {
                d3dDevice = D3D11CreateDevice(DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport, levels);
            }
            catch (SharpGenException)
            {
                d3dDevice = D3D11CreateDevice(DriverType.Warp,
                    DeviceCreationFlags.BgraSupport, levels);
            }
            d3dContext = d3dDevice.ImmediateContext;

            dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
            adapter = dxgiDevice.GetAdapter();
            dxgiFactory = adapter.GetParent<IDXGIFactory2>();
            d2dFactory = D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded, DebugLevel.None);
            d2dDevice = d2dFactory.CreateDevice(dxgiDevice);
            d2dContext = d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            compositionDevice = DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        }

        void CreateCompositionTree()
        {
            compositionDevice.CreateTargetForHwnd(hwnd, true, out compositionTarget).CheckError();
            compositionVisual = compositionDevice.CreateVisual();
            compositionTarget.SetRoot(compositionVisual).CheckError();
            compositionDevice.Commit().CheckError();
        }

        public void Resize(int newScale)
        {
            scale = Math.Max(1, newScale);
            // The swap chain is fixed to the virtual desktop. Nothing in the visual
            // tree moves per frame, so a present can never race a transform commit.
            targetWidth = virtualDesktop.Width;
            targetHeight = virtualDesktop.Height;
            DisposeSwapChain();

            var desc = new SwapChainDescription1(
                (uint)targetWidth, (uint)targetHeight, Format.B8G8R8A8_UNorm,
                false, Usage.RenderTargetOutput, BufferCount, Scaling.Stretch,
                SwapEffect.FlipSequential, Vortice.DXGI.AlphaMode.Premultiplied, SwapChainFlags.None);
            swapChain = dxgiFactory.CreateSwapChainForComposition(d3dDevice, desc, null);
            wholeClears = drawnBefore.Length;

            using (var surface = swapChain.GetBuffer<IDXGISurface>(0))
            {
                var targetProps = new BitmapProperties1(
                    new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw);
                targetBitmap = d2dContext.CreateBitmapFromDxgiSurface(surface, targetProps);
            }

            var sourceProps = new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96, 96, BitmapOptions.None);
            sourceBitmap = d2dContext.CreateBitmap(
                new SizeI(sourceWidth, sourceHeight), IntPtr.Zero,
                (uint)(sourceWidth * 4), sourceProps);
            sourceUndefined = true;
            d2dContext.Target = targetBitmap;
            compositionVisual.SetContent(swapChain).CheckError();
            compositionDevice.Commit().CheckError();
        }

        /// <param name="changed">
        /// Every canvas pixel that differs from the last frame presented lies inside this;
        /// the GPU copy of the canvas is brought up to date there and nowhere else.
        /// </param>
        public void Present(GameCanvas canvas, Rectangle changed, int screenLeft, int screenTop,
            TrailStamp[] trails, int trailCount, int foregroundStart = int.MaxValue)
        {
            if (sourceUndefined)
            {
                changed = new Rectangle(0, 0, sourceWidth, sourceHeight);
                sourceUndefined = false;
            }
            if (changed.Width > 0 && changed.Height > 0)
            {
                int bytes = changed.Width * changed.Height * 4;
                if (uploadScratch.Length < bytes) uploadScratch = new byte[bytes];
                canvas.CopyTo(changed, uploadScratch);
                sourceBitmap.CopyFromMemory(changed, uploadScratch, (uint)(changed.Width * 4)).CheckError();
            }

            // TrailManager snapshots are immutable. Upload each tiny 64x64 stamp once
            // and keep it in GPU memory until that snapshot expires.
            liveTrailBitmaps.Clear();
            for (int i = 0; i < trailCount; i++)
            {
                Bitmap source = trails[i].Bitmap;
                if (source == null) continue;
                liveTrailBitmaps.Add(source);
                if (!trailBitmaps.ContainsKey(source))
                {
                    var props = new BitmapProperties1(
                        new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                        96, 96, BitmapOptions.None);
                    var gpu = d2dContext.CreateBitmap(
                        new SizeI(source.Width, source.Height), IntPtr.Zero,
                        (uint)(source.Width * 4), props);
                    Upload(gpu, source);
                    trailBitmaps[source] = gpu;
                }
            }
            deadTrailBitmaps.Clear();
            foreach (var pair in trailBitmaps)
                if (!liveTrailBitmaps.Contains(pair.Key)) deadTrailBitmaps.Add(pair.Key);
            foreach (var dead in deadTrailBitmaps)
            {
                trailBitmaps[dead].Dispose();
                trailBitmaps.Remove(dead);
            }

            d2dContext.BeginDraw();
            bool wholeFrame = wholeClears > 0;
            if (wholeFrame)
            {
                wholeClears--;
                d2dContext.Clear(new Color4(0, 0, 0, 0));
            }
            else
            {
                Rectangle stale = Rectangle.Empty;
                foreach (Rectangle before in drawnBefore) stale = Union(stale, before);
                if (!stale.IsEmpty)
                {
                    d2dContext.PushAxisAlignedClip(new Vortice.RawRectF(stale.Left, stale.Top,
                        stale.Right, stale.Bottom), AntialiasMode.Aliased);
                    d2dContext.Clear(new Color4(0, 0, 0, 0));
                    d2dContext.PopAxisAlignedClip();
                }
            }
            drawnNow = Rectangle.Empty;
            int foreground = Math.Max(0, Math.Min(trailCount, foregroundStart));
            for (int i = 0; i < foreground; i++)
            {
                DrawStamp(trails[i]);
            }
            // Only the part of the canvas anything was drawn on: the rest is transparent, and
            // drawing transparent pixels over the target leaves it exactly as it was. Whole
            // source pixels onto whole scale-sized blocks, so the sampling is the same as for
            // the full square.
            Rectangle content = canvas.Content;
            if (content.Width > 0 && content.Height > 0)
            {
                float originX = screenLeft - virtualDesktop.Left;
                float originY = screenTop - virtualDesktop.Top;
                var destination = new Vortice.RawRectF(
                    originX + content.Left * scale, originY + content.Top * scale,
                    originX + content.Right * scale, originY + content.Bottom * scale);
                var source = new Vortice.RawRectF(content.Left, content.Top, content.Right, content.Bottom);
                d2dContext.DrawBitmap(sourceBitmap, destination, 1f,
                    Vortice.Direct2D1.InterpolationMode.NearestNeighbor, source, null);
                Touch(destination);
            }
            for (int i = foreground; i < trailCount; i++) DrawStamp(trails[i]);
            d2dContext.EndDraw().CheckError();
            Rectangle dirty = Union(drawnBefore[0], drawnNow);
            dirty.Intersect(new Rectangle(0, 0, targetWidth, targetHeight));
            for (int i = drawnBefore.Length - 1; i > 0; i--) drawnBefore[i] = drawnBefore[i - 1];
            drawnBefore[0] = drawnNow;
            // A buffer of unknown history, or nothing drawn now or before: no rectangle to
            // name, so the whole surface, as every present used to be.
            if (wholeFrame || dirty.Width <= 0 || dirty.Height <= 0)
                swapChain.Present(1, PresentFlags.None).CheckError();
            else
            {
                dirtyRectangle[0] = new Vortice.RawRect(dirty.Left, dirty.Top, dirty.Right, dirty.Bottom);
                swapChain.Present1(1, PresentFlags.None,
                    new PresentParameters { DirtyRectangles = dirtyRectangle }).CheckError();
            }

            if (!logged)
            {
                logged = true;
                PetWindow.Log("Direct3D 11 + DirectComposition active; source=" +
                    sourceWidth + "x" + sourceHeight + " desktopTarget=" +
                    targetWidth + "x" + targetHeight + " scale=" + scale);
            }

            void DrawStamp(TrailStamp trail)
            {
                if (trail.Bitmap == null || !trailBitmaps.TryGetValue(trail.Bitmap, out var gpu)) return;
                float width = trail.Bitmap.Width * scale;
                float height = trail.Bitmap.Height * scale;
                float centerX = (float)Math.Round(trail.X) * scale - virtualDesktop.Left;
                float centerY = (float)Math.Round(trail.Y) * scale - virtualDesktop.Top;
                var target = new Vortice.RawRectF(centerX - width / 2f, centerY - height / 2f,
                    centerX + width / 2f, centerY + height / 2f);
                d2dContext.DrawBitmap(gpu, target, trail.Opacity,
                    Vortice.Direct2D1.InterpolationMode.NearestNeighbor, null, null);
                Touch(target);
            }

            // Whole target pixels covering everything a draw can have reached, with a pixel
            // to spare: a stamp's edges can fall on half pixels.
            void Touch(Vortice.RawRectF area)
            {
                drawnNow = Union(drawnNow, Rectangle.FromLTRB(
                    (int)Math.Floor(area.Left) - 1, (int)Math.Floor(area.Top) - 1,
                    (int)Math.Ceiling(area.Right) + 1, (int)Math.Ceiling(area.Bottom) + 1));
            }
        }

        static Rectangle Union(Rectangle a, Rectangle b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            return Rectangle.Union(a, b);
        }

        static void Upload(ID2D1Bitmap1 destination, Bitmap bitmap)
        {
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                destination.CopyFromMemory(data.Scan0, (uint)data.Stride).CheckError();
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

        }

        void DisposeSwapChain()
        {
            d2dContext.Target = null;
            sourceBitmap?.Dispose(); sourceBitmap = null;
            foreach (var trail in trailBitmaps.Values) trail.Dispose();
            trailBitmaps.Clear();
            targetBitmap?.Dispose(); targetBitmap = null;
            swapChain?.Dispose(); swapChain = null;
        }

        public void Dispose()
        {
            if (compositionVisual != null) compositionVisual.SetContent(null);
            if (compositionTarget != null) compositionTarget.SetRoot(null);
            compositionDevice?.Commit();
            DisposeSwapChain();
            compositionVisual?.Dispose();
            compositionTarget?.Dispose();
            compositionDevice?.Dispose();
            d2dContext?.Dispose();
            d2dDevice?.Dispose();
            d2dFactory?.Dispose();
            dxgiFactory?.Dispose();
            adapter?.Dispose();
            dxgiDevice?.Dispose();
            d3dContext?.Dispose();
            d3dDevice?.Dispose();
        }
    }
}

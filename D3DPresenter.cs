using System;
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
        ID2D1Bitmap1 trailBitmap;

        int scale;
        int targetWidth;
        int targetHeight;
        int visualWidth;
        int visualHeight;
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
            visualWidth = sourceWidth * scale;
            visualHeight = sourceHeight * scale;
            // The swap chain is fixed to the virtual desktop. Nothing in the visual
            // tree moves per frame, so a present can never race a transform commit.
            targetWidth = virtualDesktop.Width;
            targetHeight = virtualDesktop.Height;
            DisposeSwapChain();

            var desc = new SwapChainDescription1(
                (uint)targetWidth, (uint)targetHeight, Format.B8G8R8A8_UNorm,
                false, Usage.RenderTargetOutput, 2, Scaling.Stretch,
                SwapEffect.FlipSequential, Vortice.DXGI.AlphaMode.Premultiplied, SwapChainFlags.None);
            swapChain = dxgiFactory.CreateSwapChainForComposition(d3dDevice, desc, null);

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
            trailBitmap = d2dContext.CreateBitmap(
                new SizeI(sourceWidth, sourceHeight), IntPtr.Zero,
                (uint)(sourceWidth * 4), sourceProps);
            d2dContext.Target = targetBitmap;
            compositionVisual.SetContent(swapChain).CheckError();
            compositionDevice.Commit().CheckError();
        }

        public void Present(Bitmap trails, Bitmap bitmap, int screenLeft, int screenTop,
            int trailScreenLeft, int trailScreenTop)
        {
            Upload(trailBitmap, trails);
            Upload(sourceBitmap, bitmap);

            d2dContext.BeginDraw();
            d2dContext.Clear(new Color4(0, 0, 0, 0));
            var trailDestination = new Vortice.RawRectF(
                trailScreenLeft - virtualDesktop.Left,
                trailScreenTop - virtualDesktop.Top,
                trailScreenLeft - virtualDesktop.Left + visualWidth,
                trailScreenTop - virtualDesktop.Top + visualHeight);
            d2dContext.DrawBitmap(trailBitmap, trailDestination, 1f,
                Vortice.Direct2D1.InterpolationMode.NearestNeighbor, null, null);
            var destination = new Vortice.RawRectF(
                screenLeft - virtualDesktop.Left,
                screenTop - virtualDesktop.Top,
                screenLeft - virtualDesktop.Left + visualWidth,
                screenTop - virtualDesktop.Top + visualHeight);
            d2dContext.DrawBitmap(sourceBitmap, destination, 1f,
                Vortice.Direct2D1.InterpolationMode.NearestNeighbor, null, null);
            d2dContext.EndDraw().CheckError();
            swapChain.Present(1, PresentFlags.None).CheckError();

            if (!logged)
            {
                logged = true;
                PetWindow.Log("Direct3D 11 + DirectComposition active; source=" +
                    sourceWidth + "x" + sourceHeight + " desktopTarget=" +
                    targetWidth + "x" + targetHeight + " scale=" + scale);
            }
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
            trailBitmap?.Dispose(); trailBitmap = null;
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

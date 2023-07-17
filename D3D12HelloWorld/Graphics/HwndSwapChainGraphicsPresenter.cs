using System;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace wired.Graphics {
    public class HwndSwapChainGraphicsPresenter : SwapChainGraphicsPresenter {
        readonly IntPtr mWindowHandle;

        public HwndSwapChainGraphicsPresenter(IDXGIFactory2 factory, GraphicsDevice device, ushort bufferCount, PresentationParameters presentationParameters, IntPtr windowHandle)
            : base(device, presentationParameters,
                   CreateSwapChain(factory, device.DirectCommandQueue.NativeCommandQueue, bufferCount, presentationParameters, windowHandle)) {
            mWindowHandle = windowHandle;
        }

        static IDXGISwapChain3 CreateSwapChain(IDXGIFactory2 factory, ID3D12CommandQueue commandQueue, ushort bufferCount, PresentationParameters presentationParameters, IntPtr windowHandle) {
            // Describe and create the swap chain.
            var swapChainDesc = new SwapChainDescription1 {
                BufferCount = bufferCount,
                BufferUsage = Usage.RenderTargetOutput,
                Format = presentationParameters.BackBufferFormat,
                Width = presentationParameters.BackBufferWidth,
                Height = presentationParameters.BackBufferHeight,
                SampleDescription = SampleDescription.Default,
                Stereo = presentationParameters.Stereo,
                SwapEffect = SwapEffect.FlipDiscard,
            };

            // Swap chain needs the queue so that it can force a flush on it.
            using IDXGISwapChain1 tempSwapChain = factory.CreateSwapChainForHwnd(commandQueue, windowHandle, swapChainDesc);
            return tempSwapChain.QueryInterface<IDXGISwapChain3>();
        }
    }
}
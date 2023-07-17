using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace wired.Graphics {
    public class SwapChainGraphicsPresenter : GraphicsPresenter {
        readonly ID3D12Device mDevice;
        readonly RenderTargetView[] mRenderTargets;

        public SwapChainGraphicsPresenter(GraphicsDevice device, PresentationParameters presentationParameters, IDXGISwapChain3 swapChain)
            : base(device, presentationParameters, new DescriptorAllocator(device.NativeDevice, DescriptorHeapType.RenderTargetView, swapChain.Description1.BufferCount)) {
            SwapChain = swapChain;

            // Create render target views (RTVs).
            mRenderTargets = new RenderTargetView[SwapChain.Description1.BufferCount];
            CreateRenderTargets(device);
            device.CommandList.SetRenderTargets(DepthStencil, mRenderTargets);

            mDevice = device.NativeDevice;
        }

        public override RenderTargetView BackBuffer => mRenderTargets[SwapChain.CurrentBackBufferIndex];

        protected IDXGISwapChain3 SwapChain { get; }

        public override void Present() {
            //SwapChain.Present(PresentationParameters.SyncInterval, PresentFlags.None, PresentationParameters.PresentParameters).CheckError();
            var presentResult = SwapChain.Present(PresentationParameters.SyncInterval, PresentFlags.None, PresentationParameters.PresentParameters);
            if (presentResult.Failure) {
                if (presentResult.Code == Vortice.DXGI.ResultCode.DeviceRemoved.Code) {
                    //Lookup code at https://docs.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-error
                    //Application should destroy and recreate the device (and all resources)
                    //I encountered DXGI_ERROR_INVALID_CALL... 0x887A0001
                    throw new COMException($"DXGI_ERROR_DEVICE_REMOVED, GetDeviceRemovedReason() yielded 0x{mDevice.DeviceRemovedReason.Code:X} {mDevice.DeviceRemovedReason.Description}.  During frame index {SwapChain.CurrentBackBufferIndex} with fence value {"??"}", presentResult.Code);
                }
                else {
                    throw new COMException($"SwapChain.Present failed 0x{presentResult.Code:X} during frame index {SwapChain.CurrentBackBufferIndex} with fence value {"??"}.", presentResult.Code);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                SwapChain.Dispose();

                foreach (RenderTargetView renderTarget in mRenderTargets) {
                    renderTarget.Resource.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        void CreateRenderTargets(GraphicsDevice device) {
            for (int i = 0; i < mRenderTargets.Length; i++) {
                var renderTargetTexture = new Texture(device, SwapChain.GetBuffer<ID3D12Resource>(i), $"{nameof(mRenderTargets)}[{i}]");
                mRenderTargets[i] = RenderTargetView.FromTexture2D(renderTargetTexture, RenderTargetViewAllocator);
            }
        }
    }
}
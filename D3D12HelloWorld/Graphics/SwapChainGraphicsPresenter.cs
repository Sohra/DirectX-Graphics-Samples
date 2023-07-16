using Vortice.Direct3D12;
using Vortice.DXGI;

namespace wired.Graphics {
    public class SwapChainGraphicsPresenter : GraphicsPresenter {
        readonly RenderTargetView[] mRenderTargets;

        public SwapChainGraphicsPresenter(GraphicsDevice device, PresentationParameters presentationParameters, IDXGISwapChain3 swapChain)
            : base(device, presentationParameters, new DescriptorAllocator(device.NativeDevice, DescriptorHeapType.RenderTargetView, swapChain.Description1.BufferCount)) {
            SwapChain = swapChain;

            // Create render target views (RTVs).
            mRenderTargets = new RenderTargetView[SwapChain.Description1.BufferCount];
            CreateRenderTargets(device);
            device.CommandList.SetRenderTargets(DepthStencil, mRenderTargets);
        }

        public override RenderTargetView BackBuffer => mRenderTargets[SwapChain.CurrentBackBufferIndex];

        protected IDXGISwapChain3 SwapChain { get; }

        public override void Present() {
            SwapChain.Present(PresentationParameters.SyncInterval, PresentFlags.None, PresentationParameters.PresentParameters).CheckError();
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
                var renderTargetTexture = new Texture(device, SwapChain.GetBuffer<ID3D12Resource>(i));
                mRenderTargets[i] = RenderTargetView.FromTexture2D(renderTargetTexture, RenderTargetViewAllocator);
                mRenderTargets[i].Resource.NativeResource.Name = $"{nameof(mRenderTargets)}[{i}]";
            }
        }
    }
}
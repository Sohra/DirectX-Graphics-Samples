using System;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace wired.Graphics {
    public abstract class GraphicsPresenter : IDisposable {
        private bool mIsDisposed;

        public GraphicsPresenter(GraphicsDevice device, PresentationParameters presentationParameters, DescriptorAllocator renderTargetViewAllocator) {
            PresentationParameters = presentationParameters.Clone();

            // Describe and create a depth stencil view (DSV) descriptor heap.
            DepthStencilViewAllocator = new DescriptorAllocator(device.NativeDevice, DescriptorHeapType.DepthStencilView, 1);

            DepthStencil = CreateDepthStencil(device, presentationParameters);
            DepthStencil.Resource.NativeResource.Name = nameof(DepthStencil);

            // Describe and create a render target view (RTV) descriptor heap.
            RenderTargetViewAllocator = renderTargetViewAllocator;
        }

        public abstract RenderTargetView BackBuffer { get; }
        public DepthStencilView DepthStencil { get; protected set; }

        public PresentationParameters PresentationParameters { get; }

        public DescriptorAllocator DepthStencilViewAllocator { get; set; }
        public DescriptorAllocator RenderTargetViewAllocator { get; set; }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public abstract void Present();

        protected virtual DepthStencilView CreateDepthStencil(GraphicsDevice device, PresentationParameters presentationParameters) {
            Texture depthStencilBuffer = CreateDepthStencilBuffer(device, presentationParameters.BackBufferWidth, presentationParameters.BackBufferHeight,
                                                                  presentationParameters.DepthStencilFormat);
            var depthStencil = DepthStencilView.FromTexture2D(depthStencilBuffer, DepthStencilViewAllocator);
            return depthStencil;
        }

        protected virtual void Dispose(bool disposing) {
            if (!mIsDisposed) {
                if (disposing) {
                    // dispose managed state (managed objects)
                    DepthStencil.Resource.Dispose();
                    RenderTargetViewAllocator.Dispose();
                    DepthStencilViewAllocator.Dispose();
                }

                mIsDisposed = true;
            }
        }

        Texture CreateDepthStencilBuffer(GraphicsDevice device, int width, int height, Format format) {
            var description = ResourceDescription.Texture2D(format, (uint)width, (uint)height, 1, 0, 1, 0, ResourceFlags.AllowDepthStencil);
            var depthOptimizedClearValue = new ClearValue(format, 1.0f, 0);
            var resource = device.NativeDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None, description,
                                                                       ResourceStates.DepthWrite, depthOptimizedClearValue);
            return new Texture(device, resource);
        }
    }
}
using Serilog;
using System;
using Vortice.Direct3D12;

namespace wired.Graphics {
    public sealed class GraphicsDevice : IDisposable {
        readonly ID3D12Device mDevice;

        public GraphicsDevice(ID3D12Device device, ILogger logger) {
            mDevice = device ?? throw new ArgumentNullException(nameof(device));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            DirectCommandQueue = new CommandQueue(this, CommandListType.Direct, "Direct Queue");
            ComputeCommandQueue = new CommandQueue(this, CommandListType.Compute, "Compute Queue");
            CopyCommandQueue = new CommandQueue(this, CommandListType.Copy, "Copy Queue");

            ShaderResourceViewAllocator = new DescriptorAllocator(device, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 4096);
            SamplerAllocator = new DescriptorAllocator(device, DescriptorHeapType.Sampler, 256);

            ShaderVisibleShaderResourceViewAllocator = new DescriptorAllocator(device, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 4096, DescriptorHeapFlags.ShaderVisible);
            ShaderVisibleSamplerAllocator = new DescriptorAllocator(device, DescriptorHeapType.Sampler, 256, DescriptorHeapFlags.ShaderVisible);

            // Create the command list.
            CommandList = new CommandList(this, CommandListType.Direct);

            // Command lists are created in the recording state, but there is nothing to record yet, so close it now.
            CommandList.Close();
        }

        internal ID3D12Device NativeDevice => mDevice;
        internal ILogger Logger { get; }

        public CommandQueue DirectCommandQueue { get; }
        public CommandQueue ComputeCommandQueue { get; }
        public CommandQueue CopyCommandQueue { get; }

        /// <summary>
        /// Gets the descriptor allocator for shader resource views. This allocator manages a descriptor heap that is not shader-visible, 
        /// typically used for creating descriptors that are used on the CPU side, such as for copying descriptors between heaps.
        /// </summary>
        public DescriptorAllocator ShaderResourceViewAllocator { get; set; }
        public DescriptorAllocator SamplerAllocator { get; set; }
        /// <summary>
        /// Gets the descriptor allocator for shader-visible shader resource views. This allocator manages a shader-visible descriptor heap, 
        /// typically used for creating descriptors that are referenced by shaders running on the GPU.
        /// </summary>
        internal DescriptorAllocator ShaderVisibleShaderResourceViewAllocator { get; }
        internal DescriptorAllocator ShaderVisibleSamplerAllocator { get; }

        public CommandList CommandList { get; }

        public void Dispose() {
            CommandList.Dispose();

            ShaderVisibleSamplerAllocator.Dispose();
            ShaderVisibleShaderResourceViewAllocator.Dispose();
            SamplerAllocator.Dispose();
            ShaderResourceViewAllocator.Dispose();

            CopyCommandQueue.Dispose();
            ComputeCommandQueue.Dispose();
            DirectCommandQueue.Dispose();

            mDevice.Dispose();
        }
    }
}
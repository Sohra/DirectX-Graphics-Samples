﻿using System;
using Vortice.Direct3D12;

namespace D3D12HelloWorld.Rendering {
    public sealed class GraphicsDevice : IDisposable {
        readonly ID3D12Device mDevice;

        public GraphicsDevice(ID3D12Device device) {
            mDevice = device ?? throw new ArgumentNullException(nameof(device));

            DirectCommandQueue = new CommandQueue(device, CommandListType.Direct, "Direct Queue");
            ComputeCommandQueue = new CommandQueue(device, CommandListType.Compute, "Compute Queue");
            CopyCommandQueue = new CommandQueue(device, CommandListType.Copy, "Copy Queue");

            DepthStencilViewAllocator = new DescriptorAllocator(device, DescriptorHeapType.DepthStencilView, 1);
            //RenderTargetViewAllocator = new DescriptorAllocator(device, DescriptorHeapType.RenderTargetView, 2);
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

        public CommandQueue DirectCommandQueue { get; }
        public CommandQueue ComputeCommandQueue { get; }
        public CommandQueue CopyCommandQueue { get; }

        public DescriptorAllocator DepthStencilViewAllocator { get; set; }
        //public DescriptorAllocator RenderTargetViewAllocator { get; set; }
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
        public DescriptorAllocator ShaderVisibleShaderResourceViewAllocator { get; }
        public DescriptorAllocator ShaderVisibleSamplerAllocator { get; }

        public CommandList CommandList { get; }

        public void Dispose() {
            CommandList.Dispose();

            ShaderVisibleSamplerAllocator.Dispose();
            ShaderVisibleShaderResourceViewAllocator.Dispose();
            SamplerAllocator.Dispose();
            ShaderResourceViewAllocator.Dispose();
            //RenderTargetViewAllocator.Dispose();
            DepthStencilViewAllocator.Dispose();

            CopyCommandQueue.Dispose();
            ComputeCommandQueue.Dispose();
            DirectCommandQueue.Dispose();

            mDevice.Dispose();
        }
    }
}
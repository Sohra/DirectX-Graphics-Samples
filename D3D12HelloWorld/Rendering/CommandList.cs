using System;
using System.Linq;
using Vortice.Direct3D12;

namespace D3D12HelloWorld.Rendering {
    public sealed class CommandList : IDisposable {
        readonly GraphicsDevice mDevice;
        readonly CommandListType mCommandListType;
        readonly ID3D12CommandAllocator mCommandAllocator;
        readonly ID3D12GraphicsCommandList mCommandList;
        DescriptorAllocator? mShaderResourceViewDescriptorHeap;
        DescriptorAllocator? mSamplerDescriptorHeap;

        public DepthStencilView? DepthStencilBuffer { get; private set; }
        public ID3D12Resource[] RenderTargets { get; private set; } = Array.Empty<ID3D12Resource>();

        public CommandList(GraphicsDevice device, CommandListType commandListType) {
            mDevice = device ?? throw new ArgumentNullException(nameof(device));
            mCommandListType = commandListType;

            mCommandAllocator = device.NativeDevice.CreateCommandAllocator(commandListType);
            mCommandAllocator.Name = $"{commandListType} Allocator ({mCommandAllocator.GetHashCode()})";

            mCommandList = device.NativeDevice.CreateCommandList<ID3D12GraphicsCommandList>(commandListType, mCommandAllocator);
            mCommandList.Name = $"{commandListType} Command List ({mCommandAllocator.GetHashCode()})";

            SetDescriptorHeaps(device.ShaderVisibleShaderResourceViewAllocator, device.ShaderVisibleSamplerAllocator);
        }

        public CompiledCommandList Close() {
            mCommandList.Close();
            return new CompiledCommandList(mCommandList, mCommandAllocator);
        }

        public void CopyBufferRegion(ID3D12Resource dstBuffer, ulong dstOffset, ID3D12Resource srcBuffer, ulong srcOffset, ulong numBytes) {
            mCommandList.CopyBufferRegion(dstBuffer, dstOffset, srcBuffer, srcOffset, numBytes);
        }

        public void CopyResource(GraphicsResource source, GraphicsResource destination) {
            mCommandList.CopyResource(destination.NativeResource, source.NativeResource);
        }

        public void Dispose() {
            mCommandList.Dispose();
            mCommandAllocator.Dispose();
        }

        public void Flush() {
            GetCommandQueue().ExecuteCommandLists(Close());
        }

        public void Reset() {
            mCommandAllocator.Reset();
            mCommandList.Reset(mCommandAllocator, null);
            SetDescriptorHeaps(mDevice.ShaderVisibleShaderResourceViewAllocator, mDevice.ShaderVisibleSamplerAllocator);
        }

        public void SetGraphicsRoot32BitConstant(int rootParameterIndex, int srcData, int destOffsetIn32BitValues) {
            mCommandList.SetGraphicsRoot32BitConstant(rootParameterIndex, srcData, destOffsetIn32BitValues);
        }

        public void SetGraphicsRootConstantBufferView(int rootParameterIndex, ConstantBufferView constantBufferView) {
            if (mShaderResourceViewDescriptorHeap == null) {
                throw new InvalidOperationException();
            }

            SetGraphicsRootDescriptorTable(rootParameterIndex, mShaderResourceViewDescriptorHeap, constantBufferView.CpuDescriptorHandle, 1);
        }

        public void SetGraphicsRootSampler(int rootParameterIndex, Sampler sampler) {
            if (mSamplerDescriptorHeap == null) {
                throw new InvalidOperationException();
            }

            SetGraphicsRootDescriptorTable(rootParameterIndex, mSamplerDescriptorHeap, sampler.CpuDescriptorHandle, 1);
        }

        public void SetGraphicsRootDescriptorTable(int rootParameterIndex, DescriptorSet descriptorSet) {
            DescriptorAllocator? descriptorAllocator = (descriptorSet.DescriptorHeapType == DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView)
                                                     ? mShaderResourceViewDescriptorHeap
                                                     : mSamplerDescriptorHeap;
            if (descriptorAllocator == null) {
                throw new InvalidOperationException();
            }

            SetGraphicsRootDescriptorTable(rootParameterIndex, descriptorAllocator, descriptorSet.StartCpuDescriptorHandle, descriptorSet.DescriptorCapacity);
        }

        private void SetGraphicsRootDescriptorTable(int rootParameterIndex, DescriptorAllocator descriptorAllocator, CpuDescriptorHandle baseDescriptor, int descriptorCount) {
            GpuDescriptorHandle value = CopyDescriptors(descriptorAllocator, baseDescriptor, descriptorCount);
            mCommandList.SetGraphicsRootDescriptorTable(rootParameterIndex, value);
        }

        public ulong UpdateSubresource(ID3D12Device device, ID3D12Resource destResource, ID3D12Resource intermediateResource,
                                       ulong intermediateOffset, int firstSubresource, ReadOnlySpan<byte> data)
            => device.UpdateSubresource(mCommandList, destResource, intermediateResource, intermediateOffset, firstSubresource, data);

        GpuDescriptorHandle CopyDescriptors(DescriptorAllocator descriptorAllocator, CpuDescriptorHandle baseDescriptor, int descriptorCount) {
            CpuDescriptorHandle intPtr = descriptorAllocator.Allocate(descriptorCount);
            mDevice.NativeDevice.CopyDescriptorsSimple(descriptorCount, intPtr, baseDescriptor, descriptorAllocator.DescriptorHeap.Description.Type);
            return descriptorAllocator.GetGpuDescriptorHandle(intPtr);
        }

        CommandQueue GetCommandQueue() {
            CommandListType commandListType = mCommandListType;

            CommandQueue result = commandListType switch {
                CommandListType.Direct => mDevice.DirectCommandQueue,
                CommandListType.Compute => mDevice.ComputeCommandQueue,
                CommandListType.Copy => mDevice.CopyCommandQueue,
                _ => throw new NotSupportedException(),
            };

            return result;
        }

        void SetDescriptorHeaps(params DescriptorAllocator[] descriptorHeaps) {
            if (mCommandListType != CommandListType.Copy) {
                mCommandList.SetDescriptorHeaps(descriptorHeaps.Length, descriptorHeaps.Select((DescriptorAllocator d) => d.DescriptorHeap).ToArray());
                mShaderResourceViewDescriptorHeap = descriptorHeaps.SingleOrDefault((DescriptorAllocator d) => d.DescriptorHeap.Description.Type == DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
                mSamplerDescriptorHeap = descriptorHeaps.SingleOrDefault((DescriptorAllocator d) => d.DescriptorHeap.Description.Type == DescriptorHeapType.Sampler);
            }
        }

    }
}
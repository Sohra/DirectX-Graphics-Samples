﻿using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace wired.Graphics {
    public sealed class CommandList : IDisposable {
        readonly GraphicsDevice mDevice;
        readonly CommandListType mCommandListType;
        readonly ID3D12CommandAllocator mCommandAllocator;
        readonly ID3D12GraphicsCommandList mCommandList;
        bool mIsCommandListClosed;
        DescriptorAllocator? mShaderResourceViewDescriptorHeap;
        DescriptorAllocator? mSamplerDescriptorHeap;

        public DepthStencilView? DepthStencilBuffer { get; private set; }
        public RenderTargetView[] RenderTargets { get; private set; } = Array.Empty<RenderTargetView>();
        public Rectangle[] ScissorRectangles { get; private set; } = Array.Empty<Rectangle>();
        public Viewport[] Viewports { get; private set; } = Array.Empty<Viewport>();

        public CommandList(GraphicsDevice device, CommandListType commandListType, PipelineState? initialState = null) {
            mDevice = device ?? throw new ArgumentNullException(nameof(device));
            mCommandListType = commandListType;

            mCommandAllocator = device.NativeDevice.CreateCommandAllocator(commandListType);
            mCommandAllocator.Name = $"{commandListType} Allocator ({mCommandAllocator.GetHashCode()})";

            mCommandList = device.NativeDevice.CreateCommandList<ID3D12GraphicsCommandList>(commandListType, mCommandAllocator, initialState?.NativePipelineState);
            mCommandList.Name = $"{commandListType} Command List ({mCommandAllocator.GetHashCode()})";

            SetDescriptorHeaps(device.ShaderVisibleShaderResourceViewAllocator, device.ShaderVisibleSamplerAllocator);
        }

        public void ClearDepthStencilView(DepthStencilView depthStencilView, ClearFlags clearFlags, float depth = 1.0f, byte stencil = 0, params Rectangle[] rectangles) {
            mCommandList.ClearDepthStencilView(depthStencilView.CpuDescriptorHandle, clearFlags, depth, stencil, rectangles.Select(r => new RawRect(r.Left, r.Top, r.Right, r.Bottom)).ToArray());
        }

        public void ClearRenderTargetView(RenderTargetView renderTargetView, in Vector4 color, params Rectangle[] rectangles) {
            mCommandList.ClearRenderTargetView(renderTargetView.CpuDescriptorHandle, new Color4(color), rectangles.Select(r => new RawRect(r.Left, r.Top, r.Right, r.Bottom)).ToArray());
        }

        public CompiledCommandList Close() {
            if (mIsCommandListClosed) {
                mDevice.Logger.Warning("Attempt to close already closed {CommandListName}.", mCommandList.Name);
            }
            else {
                mCommandList.Close();
                mIsCommandListClosed = true;
            }
            return new CompiledCommandList(this, mCommandAllocator, mCommandList);
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

        public void DrawIndexedInstanced(int indexCountPerInstance, int instanceCount, int startIndexLocation = 0, int baseVertexLocation = 0, int startInstanceLocation = 0) {
            mCommandList.DrawIndexedInstanced(indexCountPerInstance, instanceCount, startIndexLocation, baseVertexLocation, startInstanceLocation);
        }

        public void DrawInstanced(int vertexCountPerInstance, int instanceCount, int startVertexLocation = 0, int startInstanceLocation = 0) {
            mCommandList.DrawInstanced(vertexCountPerInstance, instanceCount, startVertexLocation, startInstanceLocation);
        }

        public void ExecuteBundle(CompiledCommandList commandList) {
            if (commandList.Builder.mCommandListType == CommandListType.Bundle) {
                mCommandList.ExecuteBundle(commandList.NativeCommandList);
            }
        }

        public void Flush() {
            GetCommandQueue().ExecuteCommandLists(Close());
        }

        public void Reset() {
            mCommandAllocator.Reset();
            mCommandList.Reset(mCommandAllocator, null);
            mIsCommandListClosed = false;
            SetDescriptorHeaps(mDevice.ShaderVisibleShaderResourceViewAllocator, mDevice.ShaderVisibleSamplerAllocator);
        }

        [Obsolete("Work to refactor off this in favour of Reset() and SetPipelineState(PipelineState), which only uses our own allocator, and which sets the root signature according to the pipeline state.")]
        public void Reset(ID3D12CommandAllocator commandAllocator, PipelineState pipelineState) {
            mCommandList.Reset(commandAllocator, pipelineState.NativePipelineState);
            mIsCommandListClosed = false;
        }

        [Obsolete("Work to refactor off this in favour of SetPipelineState(PipelineState), which sets the root signature according to the pipeline state, and which sets the descriptor heaps for the shaders and samplers managed by GraphicsDevice.")]
        public void SetNecessaryState(ID3D12RootSignature rootSignature, params ID3D12DescriptorHeap[] descriptorHeaps) {
            mCommandList.SetGraphicsRootSignature(rootSignature);
            mCommandList.SetDescriptorHeaps(descriptorHeaps);
        }

        public void ResourceBarrierTransition(GraphicsResource resource, ResourceStates stateBefore, ResourceStates stateAfter) {
            mCommandList.ResourceBarrierTransition(resource.NativeResource, stateBefore, stateAfter);
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

        public void SetIndexBuffer(IndexBufferView? indexBufferView) {
            mCommandList.IASetIndexBuffer(indexBufferView);
        }

        public void SetPipelineState(PipelineState pipelineState) {
            if (pipelineState.IsCompute) {
                mCommandList.SetComputeRootSignature(pipelineState.RootSignature);
            }
            else {
                mCommandList.SetGraphicsRootSignature(pipelineState.RootSignature);
            }

            mCommandList.SetPipelineState(pipelineState.NativePipelineState);
        }

        public void SetPrimitiveTopology(PrimitiveTopology primitiveTopology) {
            mCommandList.IASetPrimitiveTopology(primitiveTopology);
        }

        public void SetRenderTargets(DepthStencilView? depthStencilView, params RenderTargetView[] renderTargetViews) {
            DepthStencilBuffer = depthStencilView;

            if (RenderTargets.Length != renderTargetViews.Length) {
                RenderTargets = new RenderTargetView[renderTargetViews.Length];
            }

            renderTargetViews.CopyTo(RenderTargets, 0);

            var renderTargetDescriptors = new CpuDescriptorHandle[renderTargetViews.Length];

            for (int i = 0; i < renderTargetViews.Length; i++) {
                renderTargetDescriptors[i] = renderTargetViews[i].CpuDescriptorHandle;
            }

            if (!mIsCommandListClosed) {
                mCommandList.OMSetRenderTargets(renderTargetDescriptors, depthStencilView?.CpuDescriptorHandle);
            }
        }

        public void SetScissorRectangles(params Rectangle[] scissorRectangles) {
            if (scissorRectangles.Length > D3D12.ViewportAndScissorRectObjectCountPerPipeline) {
                throw new ArgumentOutOfRangeException(nameof(scissorRectangles), scissorRectangles.Length, $"The maximum number of scissor rectangles is {D3D12.ViewportAndScissorRectObjectCountPerPipeline}.");
            }

            if (ScissorRectangles.Length != scissorRectangles.Length) {
                ScissorRectangles = new Rectangle[scissorRectangles.Length];
            }

            scissorRectangles.CopyTo(ScissorRectangles, 0);

            mCommandList.RSSetScissorRects(scissorRectangles.Select(r => new RawRect(r.Left, r.Top, r.Right, r.Bottom)).ToArray());
        }

        public void SetVertexBuffers(int startSlot, params VertexBufferView[] vertexBufferViews) {
            mCommandList.IASetVertexBuffers(startSlot, vertexBufferViews);
        }

        public void SetViewports(params Viewport[] viewports) {
            if (viewports.Length > D3D12.ViewportAndScissorRectObjectCountPerPipeline) {
                throw new ArgumentOutOfRangeException(nameof(viewports), viewports.Length, $"The maximum number of viewports is {D3D12.ViewportAndScissorRectObjectCountPerPipeline}.");
            }

            if (Viewports.Length != viewports.Length) {
                Viewports = new Viewport[viewports.Length];
            }

            viewports.CopyTo(Viewports, 0);

            mCommandList.RSSetViewports(viewports);
        }

        public ulong UpdateSubresource(GraphicsResource destResource, ID3D12Resource intermediateResource,
                                       ulong intermediateOffset, int firstSubresource, ReadOnlySpan<byte> data)
            => mDevice.NativeDevice.UpdateSubresource(mCommandList, destResource.NativeResource, intermediateResource, intermediateOffset, firstSubresource, data);

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

        [Obsolete("Work to refactor off this in favour of SetRenderTargets(DepthStencilView?, params RenderTargetView[]), which also updates the same named public properties on this instance so as to affect newly created pipelinestates etc accordingly.")]
        internal void OMSetRenderTargets(CpuDescriptorHandle renderTargetDescriptor, CpuDescriptorHandle? depthStencilDescriptor) {
            mCommandList.OMSetRenderTargets(renderTargetDescriptor, depthStencilDescriptor);
        }

        [Obsolete("Work to refactor off this in favour of SetGraphicsRootDescriptorTable(int, DescriptorSet).")]
        internal void SetGraphicsRootDescriptorTable(int rootParameterIndex, GpuDescriptorHandle baseDesciptor) {
            mCommandList.SetGraphicsRootDescriptorTable(rootParameterIndex, baseDesciptor);
        }
    }
}
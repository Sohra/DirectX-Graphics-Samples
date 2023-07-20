using Serilog;
using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;
using wired.Games;
using wired.Graphics;

namespace D3D12HelloWorld.Mutiny {
    internal class FrameResource : IDisposable {
        const int BufferCount = 1;

        readonly GraphicsResource mCbvUploadHeap;
        readonly ConstantBufferView[] mConstantBufferViews;

        ulong mFenceValue;
        bool mIsDisposed;

        public FrameResource(GraphicsDevice device, string? name = null) {
            // The command allocator is used by the main sample class when 
            // resetting the command list in the main update loop. Each frame 
            // resource needs a command allocator because command allocators 
            // cannot be reused until the GPU is done executing the commands 
            // associated with it.
            CommandAllocator = device.NativeDevice.CreateCommandAllocator(CommandListType.Direct);
            if (!string.IsNullOrWhiteSpace(name)) {
                CommandAllocator.Name = $"{name} Direct Allocator ({CommandAllocator.GetHashCode()})";
            }

            // Create an upload heap for the constant buffers.
            mCbvUploadHeap = GraphicsResource.CreateBuffer(device, (Unsafe.SizeOf<Matrix4x4>() + 255) & ~255,
                                                           ResourceFlags.None, HeapType.Upload);
            mCbvUploadHeap.NativeResource.Name = nameof(mCbvUploadHeap);

            // Map the constant buffers. Note that unlike D3D11, the resource 
            // does not need to be unmapped for use by the GPU. In this sample, 
            // the resource stays 'permanently' mapped to avoid overhead with 
            // mapping/unmapping each frame.
            mCbvUploadHeap.Map(0);

            // Create CBVs
            mConstantBufferViews = CreateConstantBufferViews(device);
        }

        public ID3D12CommandAllocator CommandAllocator { get; private set; }

        /// <summary>
        /// Schedules a signal on the GPU command queue. This method can be used in combination with <see cref="WaitForSignal(CommandQueue)"/> to wait for the GPU to complete work up to this point.
        /// </summary>
        /// <param name="commandQueue"></param>
        public void AddSignal(CommandQueue commandQueue) {
            // Signal and increment the fence value.
            mFenceValue = commandQueue.AddSignal();
        }

        public IDisposable CreateProfilingEvent(CommandQueue commandQueue, string eventName, int frameIndex, ILogger mLogger) {
            return new ProfilingEvent(commandQueue.NativeCommandQueue, $"{eventName} Frame index: {frameIndex} with fence value {mFenceValue}", mLogger);
        }

        /// <summary>
        /// Make sure that this frame resource isn't still in use by the GPU.
        /// If it is, wait for it to complete, because resources still scheduled for GPU execution
        /// cannot be modified or else undefined behavior will result.
        /// </summary>
        /// <param name="commandQueue"></param>
        public void WaitForSignal(CommandQueue commandQueue) {
            if (mFenceValue != 0) {
                commandQueue.WaitForSignal(mFenceValue);
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void RecordCommandList(wired.Rendering.Model model, CommandList commandList, GraphicsResource[] worldMatrixBuffers, int instanceCount) {
            int renderTargetCount = 1; //We don't do stereo so this is always 1
            instanceCount *= renderTargetCount;

            for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++) {
                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[mesh.MaterialIndex];

                // If the root signature matches the root signature of the caller, then
                // bindings are inherited, otherwise the bind space is reset.
                commandList.SetPipelineState(material.PipelineState!, true);

                commandList.SetShaderVisibleDescriptorHeaps();

                commandList.SetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
                commandList.SetIndexBuffer(mesh.MeshDraw.IndexBufferView);
                commandList.SetVertexBuffers(0, mesh.MeshDraw.VertexBufferViews!);


                int rootParameterIndex = 0;
                commandList.SetGraphicsRootConstantBufferViewGpuBound(rootParameterIndex++, mConstantBufferViews[0]);

                if (material.ShaderResourceViewDescriptorSet != null) {
                    commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, material.ShaderResourceViewDescriptorSet);
                }

                if (material.SamplerDescriptorSet != null) {
                    commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, material.SamplerDescriptorSet);
                }

                if (mesh.MeshDraw.IndexBufferView != null) {
                    commandList.DrawIndexedInstanced(mesh.MeshDraw.IndexBufferView.Value.SizeInBytes / (mesh.MeshDraw.IndexBufferView.Value.Format.GetBitsPerPixel() >> 3), instanceCount);
                }
                else {
                    var firstVertexBufferView = mesh.MeshDraw.VertexBufferViews!.First();
                    commandList.DrawInstanced(firstVertexBufferView.SizeInBytes / firstVertexBufferView.StrideInBytes, instanceCount);
                }
            }
        }


        public void UpdateConstantBuffers(Matrix4x4 view, Matrix4x4 projection) {
            for (ulong i = 0; i < BufferCount; i++) {
                Matrix4x4 model = Matrix4x4.CreateTranslation(0.0f, 0.0f, 0.0f);

                // Compute the model-view-projection matrix.
                Matrix4x4 mvp = model * view * projection;

                // Copy this matrix into the appropriate location in the upload heap subresource.
                mConstantBufferViews[i].Resource.SetData(mvp, (uint)(i * mConstantBufferViews[0].Resource.SizeInBytes));
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (!mIsDisposed) {
                if (disposing) {
                    // dispose managed state (managed objects)
                    mCbvUploadHeap.Unmap(0);
                }

                // set large fields to null
                mIsDisposed = true;
            }
        }

        ConstantBufferView[] CreateConstantBufferViews(GraphicsDevice device) {
            var constantBufferViews = new ConstantBufferView[BufferCount];
            var constantBufferSize = mCbvUploadHeap.SizeInBytes;
            ulong cbOffset = 0;
            for (var i = 0; i < constantBufferViews.Length; i++) {
                constantBufferViews[i] = new ConstantBufferView(mCbvUploadHeap, cbOffset, constantBufferSize, device.ShaderVisibleShaderResourceViewAllocator);

                // Increment the offset for the next one
                cbOffset += constantBufferSize;
            }
            return constantBufferViews;
        }
    }
}
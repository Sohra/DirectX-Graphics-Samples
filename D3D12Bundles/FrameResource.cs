using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using wired.Graphics;

namespace D3D12Bundles {
    internal class FrameResource : IDisposable {
        internal ID3D12CommandAllocator mCommandAllocator;
        internal CommandList mBundle;
        internal ID3D12Resource mCbvUploadHeap;
        IntPtr mpConstantBuffers;
        internal ulong mFenceValue;

        readonly Matrix4x4[] mModelMatrices;
        readonly int mCityRowCount;
        readonly int mCityColumnCount;
        private bool mIsDisposed;

        public unsafe struct SceneConstantBuffer {
            public Matrix4x4 mvp; //Model-view-projection (MVP) matrix;
            public fixed float padding[48];

            public SceneConstantBuffer() {
                mvp = Matrix4x4.Identity;
            }
        }

        public FrameResource(ID3D12Device device, int cityRowCount, int cityColumnCount) {
            mCityRowCount = cityRowCount;
            mCityColumnCount = cityColumnCount;

            mModelMatrices = new Matrix4x4[mCityRowCount * mCityColumnCount];


            // The command allocator is used by the main sample class when 
            // resetting the command list in the main update loop. Each frame 
            // resource needs a command allocator because command allocators 
            // cannot be reused until the GPU is done executing the commands 
            // associated with it.
            mCommandAllocator = device.CreateCommandAllocator(CommandListType.Direct);

            // Create an upload heap for the constant buffers.
            mCbvUploadHeap = device.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                            ResourceDescription.Buffer(Unsafe.SizeOf<SceneConstantBuffer>() * mCityRowCount * mCityColumnCount),
                                                            ResourceStates.GenericRead, null);

            // Map the constant buffers. Note that unlike D3D11, the resource 
            // does not need to be unmapped for use by the GPU. In this sample, 
            // the resource stays 'permenantly' mapped to avoid overhead with 
            // mapping/unmapping each frame.
            //mCbvUploadHeap.Map(0, readRange, mpConstantBuffers).CheckError();
            var readRange = new Vortice.Direct3D12.Range(0, 0); // We do not intend to read from this resource on the CPU.
            unsafe {
                void* mappedResource;
                mCbvUploadHeap.Map(0, readRange, &mappedResource).CheckError();

                mpConstantBuffers = new IntPtr(mappedResource);
            }

            // Update all of the model matrices once; our cities don't move so 
            // we don't need to do this ever again.
            SetCityPositions(8.0f, -8.0f);
        }

        protected virtual void Dispose(bool disposing) {
            if (!mIsDisposed) {
                if (disposing) {
                    // dispose managed state (managed objects)
                    mCbvUploadHeap.Unmap(0, null);
                    mBundle.Dispose();
                }

                // free unmanaged resources (unmanaged objects) and override finalizer
                mpConstantBuffers = IntPtr.Zero;

                // TODO: set large fields to null
                mIsDisposed = true;
            }
        }

        ~FrameResource() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void InitBundle(GraphicsDevice device, PipelineState pso1, PipelineState pso2, int frameResourceIndex, int numIndices, IndexBufferView? indexBufferViewDesc,
                               VertexBufferView vertexBufferViewDesc, ID3D12DescriptorHeap cbvSrvDescriptorHeap, int cbvSrvDescriptorSize, ID3D12DescriptorHeap samplerDescriptorHeap, ID3D12RootSignature rootSignature) {
            mBundle = new CommandList(device, CommandListType.Bundle, pso1); 

            PopulateCommandList(mBundle, pso1, pso2, frameResourceIndex, numIndices, indexBufferViewDesc,
                                vertexBufferViewDesc, cbvSrvDescriptorHeap, cbvSrvDescriptorSize, samplerDescriptorHeap, rootSignature);

            mBundle.Close();
        }

        public void PopulateCommandList(CommandList commandList, PipelineState pso1, PipelineState pso2, int frameResourceIndex,
                                        int numIndices, IndexBufferView? indexBufferViewDesc, VertexBufferView vertexBufferViewDesc,
                                        ID3D12DescriptorHeap cbvSrvDescriptorHeap, int cbvSrvDescriptorSize,
                                        ID3D12DescriptorHeap samplerDescriptorHeap, ID3D12RootSignature rootSignature) {
            // If the root signature matches the root signature of the caller, then
            // bindings are inherited, otherwise the bind space is reset.
            commandList.SetNecessaryState(rootSignature, cbvSrvDescriptorHeap, samplerDescriptorHeap);

            commandList.SetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            commandList.SetIndexBuffer(indexBufferViewDesc);
            commandList.SetVertexBuffers(0, vertexBufferViewDesc);
            commandList.SetGraphicsRootDescriptorTable(0, cbvSrvDescriptorHeap.GetGPUDescriptorHandleForHeapStart());
            commandList.SetGraphicsRootDescriptorTable(1, samplerDescriptorHeap.GetGPUDescriptorHandleForHeapStart());

            // Calculate the descriptor offset due to multiple frame resources.
            // 1 SRV + how many CBVs we have currently.
            var frameResourceDescriptorOffset = 1 + frameResourceIndex * mCityRowCount * mCityColumnCount;

            GpuDescriptorHandle cbvSrvHandle = cbvSrvDescriptorHeap.GetGPUDescriptorHandleForHeapStart().Offset(frameResourceDescriptorOffset, cbvSrvDescriptorSize);

            using (var pe = new ProfilingEvent(commandList, "Draw cities")) {
                var usePso1 = true;
                for (var i = 0; i < mCityRowCount; i++) {
                    for (var j = 0; j < mCityColumnCount; j++) {
                        // Alternate which PSO to use; the pixel shader is different on 
                        // each just as a PSO setting demonstration.
                        commandList.SetPipelineState(usePso1 ? pso1 : pso2);
                        usePso1 = !usePso1;

                        // Set this city's CBV table and move to the next descriptor.
                        commandList.SetGraphicsRootDescriptorTable(2, cbvSrvHandle);
                        cbvSrvHandle.Offset(cbvSrvDescriptorSize);

                        commandList.DrawIndexedInstanced(numIndices, 1, 0, 0, 0);
                    }
                }
            }
        }

        public void UpdateConstantBuffers(Matrix4x4 view, Matrix4x4 projection) {
            for (var i = 0; i < mCityRowCount; i++) {
                for (var j = 0; j < mCityColumnCount; j++) {
                    Matrix4x4 model = mModelMatrices[i * mCityColumnCount + j];

                    // Compute the model-view-projection matrix.
                    //C++ code did this, but I think I need to remove it for C#, I guess System.Numerics is row-major?
                    //Matrix4x4 mvp = Matrix4x4.Transpose(model * view * projection);
                    Matrix4x4 mvp = model * view * projection;

                    // Copy this matrix into the appropriate location in the upload heap subresource.
                    //memcpy(&mpConstantBuffers[i * mCityColumnCount + j], &mvp, Unsafe.SizeOf(mvp));
                    Marshal.StructureToPtr(mvp, mpConstantBuffers + (i * mCityColumnCount + j) * Unsafe.SizeOf<Matrix4x4>(), false);
                    //Above is blank, if I skip the view and projection matrices, I can at least see the model...
                    //Marshal.StructureToPtr(model, mpConstantBuffers + (i * mCityColumnCount + j) * Unsafe.SizeOf<Matrix4x4>(), false);
                }
            }
        }

        void SetCityPositions(float intervalX, float intervalZ) {
            for (var i = 0; i < mCityRowCount; i++) {
                var cityOffsetZ = i * intervalZ;
                for (var j = 0; j < mCityColumnCount; j++) {
                    var cityOffsetX = j * intervalX;

                    // The y position is based off of the city's row and column 
                    // position to prevent z-fighting.
                    mModelMatrices[i * mCityColumnCount + j] = Matrix4x4.CreateTranslation(cityOffsetX, 0.02f * (i * mCityColumnCount + j), cityOffsetZ);
                }
            }
        }

    }
}
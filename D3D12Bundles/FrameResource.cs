using Serilog;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using wired.Graphics;

namespace D3D12Bundles {
    internal class FrameResource : IDisposable {
        readonly ILogger mLogger;
        readonly GraphicsResource mCbvUploadHeap;
        readonly Matrix4x4[] mModelMatrices;
        readonly ConstantBufferView[] mConstantBufferViews;
        readonly int mCityRowCount;
        readonly int mCityColumnCount;

        CompiledCommandList? mBundle;
        internal ulong mFenceValue;
        bool mIsDisposed;

        public FrameResource(GraphicsDevice device, int cityRowCount, int cityColumnCount, string? name = null) {
            mLogger = device.Logger;
            mCityRowCount = cityRowCount;
            mCityColumnCount = cityColumnCount;

            mModelMatrices = new Matrix4x4[mCityRowCount * mCityColumnCount];


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
            mCbvUploadHeap = GraphicsResource.CreateBuffer(device, Unsafe.SizeOf<SceneConstantBuffer>() * mCityRowCount * mCityColumnCount,
                                                           ResourceFlags.None, HeapType.Upload);
            mCbvUploadHeap.NativeResource.Name = nameof(mCbvUploadHeap);

            // Map the constant buffers. Note that unlike D3D11, the resource 
            // does not need to be unmapped for use by the GPU. In this sample, 
            // the resource stays 'permanently' mapped to avoid overhead with 
            // mapping/unmapping each frame.
            mCbvUploadHeap.Map(0);

            // Update all of the model matrices once; our cities don't move so 
            // we don't need to do this ever again.
            SetCityPositions(8.0f, -8.0f);

            // Create CBVs
            mConstantBufferViews = CreateConstantBufferViews(device);
        }

        public ID3D12CommandAllocator CommandAllocator { get; set; }

        public CompiledCommandList Bundle {
            get {
                return mBundle ?? throw new InvalidOperationException($"Bundle is not initialised!  Call {nameof(InitBundle)} before attempting to use this property.");
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void InitBundle(GraphicsDevice device, PipelineState pso1, PipelineState pso2, int numIndices, IndexBufferView? indexBufferViewDesc,
                               VertexBufferView vertexBufferViewDesc, DescriptorSet shaderResourceViewDescriptorSet, Sampler sampler) {
            var bundle = new CommandList(device, CommandListType.Bundle, pso1); 

            PopulateCommandList(bundle, pso1, pso2, numIndices, indexBufferViewDesc,
                                vertexBufferViewDesc, shaderResourceViewDescriptorSet, sampler);

            mBundle = bundle.Close();
        }

        public void PopulateCommandList(CommandList commandList, PipelineState pso1, PipelineState pso2,
                                        int numIndices, IndexBufferView? indexBufferViewDesc, VertexBufferView vertexBufferViewDesc,
                                        DescriptorSet shaderResourceViewDescriptorSet, Sampler sampler) {
            // If the root signature matches the root signature of the caller, then
            // bindings are inherited, otherwise the bind space is reset.
            commandList.SetRootSignature(pso1);

            commandList.SetShaderVisibleDescriptorHeaps();

            commandList.SetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            commandList.SetIndexBuffer(indexBufferViewDesc);
            commandList.SetVertexBuffers(0, vertexBufferViewDesc);

            commandList.SetGraphicsRootDescriptorTable(0, shaderResourceViewDescriptorSet);
            commandList.SetGraphicsRootSampler(1, sampler);
            using (var pe = new ProfilingEvent(commandList, "Draw cities", mLogger)) {
                var usePso1 = true;
                for (var i = 0; i < mCityRowCount; i++) {
                    for (var j = 0; j < mCityColumnCount; j++) {
                        // Alternate which PSO to use; the pixel shader is different on 
                        // each just as a PSO setting demonstration.
                        commandList.SetPipelineState(usePso1 ? pso1 : pso2, false);
                        usePso1 = !usePso1;

                        // Set this city's CBV table and move to the next descriptor.
                        commandList.SetGraphicsRootConstantBufferViewGpuBound(2, mConstantBufferViews[i * mCityColumnCount + j]);

                        mLogger.Debug("Rendering instance {InstanceNum} at {Translation}", i * mCityColumnCount + j, mModelMatrices[i * mCityColumnCount + j].Translation);
                        commandList.DrawIndexedInstanced(numIndices, 1, 0, 0, 0);
                    }
                }
            }
        }

        public void UpdateConstantBuffers(Matrix4x4 view, Matrix4x4 projection, bool isUsingRawHlslShaders) {
            for (var i = 0; i < mCityRowCount; i++) {
                for (var j = 0; j < mCityColumnCount; j++) {
                    Matrix4x4 model = mModelMatrices[i * mCityColumnCount + j];

                    // Compute the model-view-projection matrix.
                    // C++ code did this, and I initially thought I need to remove it for C# because System.Numerics is column-major,
                    // whereas HLSL uses row-major.  However it turns out that it's nothing to do with C# vs C++ differences, but rather
                    // HLSL vs .NET.  When using the original HLSL vertex shader we must transpose mvp before upload, however if compiling,
                    // a .NET shader to HLSL, it seems that process automatically transposes and we must remove this call...
                    // Allegedly there isn HLSL directive: #pragma pack_matrix (column_major)
                    // which can be used to negate the need for this.
                    Matrix4x4 mvp = model * view * projection;

                    if (isUsingRawHlslShaders)
                        mvp = Matrix4x4.Transpose(mvp);

                    // Copy this matrix into the appropriate location in the upload heap subresource.
                    mConstantBufferViews[i * mCityColumnCount + j].Resource.SetData(mvp, (uint)((i * mCityColumnCount + j) * Unsafe.SizeOf<Matrix4x4>()));
                }
            }
        }

        protected virtual void Dispose(bool disposing) {
            if (!mIsDisposed) {
                if (disposing) {
                    // dispose managed state (managed objects)
                    mBundle?.Builder.Dispose();
                    mCbvUploadHeap.Unmap(0);
                }

                // free unmanaged resources (unmanaged objects)
                // Nothing to free

                // set large fields to null
                mBundle = null;
                mIsDisposed = true;
            }
        }

        unsafe struct SceneConstantBuffer {
            public Matrix4x4 mvp; //Model-view-projection (MVP) matrix;
            public fixed float padding[48];

            public SceneConstantBuffer() {
                mvp = Matrix4x4.Identity;
            }
        }

        ~FrameResource() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        ConstantBufferView[] CreateConstantBufferViews(GraphicsDevice device) {
            var constantBufferViews = new ConstantBufferView[mCityRowCount * mCityColumnCount];
            var sceneConstantBufferSize = (ulong)Unsafe.SizeOf<SceneConstantBuffer>();
            ulong cbOffset = 0;
            for (var i = 0; i < mCityRowCount; i++) {
                for (var j = 0; j < mCityColumnCount; j++) {
                    // Describe and create a constant buffer view (CBV).
                    constantBufferViews[i * mCityColumnCount + j] = new ConstantBufferView(mCbvUploadHeap, cbOffset, sceneConstantBufferSize, device.ShaderVisibleShaderResourceViewAllocator);

                    // Increment the offset for the next one
                    cbOffset += sceneConstantBufferSize;
                }
            }
            return constantBufferViews;
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
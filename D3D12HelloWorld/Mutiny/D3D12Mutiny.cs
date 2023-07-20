using Serilog;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media;
using Vortice;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using wired.Assets;
using wired.Games;
using wired.Graphics;
using wired.Rendering;

namespace D3D12HelloWorld.Mutiny {
    public partial class D3D12Mutiny : Form {
        const int FrameCount = 2;

        readonly string mName;
        readonly ILogger mLogger;

        //Viewport dimensions
        float mAspectRatio;

        //Adapter info
        bool mUseWarpDevice;

        // Pipeline objects.
        Viewport mViewport;
        RawRect mScissorRect;
        GraphicsDevice mGraphicsDevice;
        GraphicsPresenter mPresenter;
        ID3D12RootSignature mRootSignature;  //Potentially replaceable by MaterialPass.PipelineState
        PipelineState mPipelineState;

        // App resources.
        GraphicsResource mCbvUploadHeap;
        ConstantBufferView[] mConstantBufferViews;
        GraphicsResource mViewProjectionTransformBuffer;
        ID3D12Resource mIndexBuffer;
        ID3D12Resource mVertexBuffer;
        IEnumerable<ShaderResourceView> mShaderResourceViews;
        Model mModel;

        // Frame resources.
        readonly FrameResource[] mFrameResources;
        FrameResource mCurrentFrameResource;
        int mCurrentFrameResourceIndex;

        // Synchronization objects.
        StepTimer mTimer;
        int mFrameCounter;

        //GameBase
        private readonly object mTickLock = new object();

        public D3D12Mutiny() : this(1200, 900, string.Empty, Log.Logger) {
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public D3D12Mutiny(uint width, uint height, string name, ILogger logger) {
            InitializeComponent();

            Width = Convert.ToInt32(width);
            Height = Convert.ToInt32(height);
            mName = name;
            mLogger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!string.IsNullOrEmpty(mName))
                Text = mName;

            mAspectRatio = width / (float)height;

            mViewport = new Viewport(width, height);
            mScissorRect = new RawRect(0, 0, Width, Height);

            mTimer = new StepTimer();
            mFrameResources = new FrameResource[FrameCount];

            OnInit();

            CompositionTarget.Rendering += HandleCompositionTarget_Rendering;
            this.FormClosing += (object? sender, FormClosingEventArgs e) => OnDestroy();
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private void HandleCompositionTarget_Rendering(object? sender, EventArgs e) {
            lock (mTickLock) {
                OnUpdate();

                OnRender();
            }
        }

        public virtual void OnInit() {
            LoadPipeline();
            LoadAssets();
        }

        /// <summary>
        /// Update frame-based values
        /// </summary>
        public virtual void OnUpdate() {
            mTimer.Tick();

            if (mFrameCounter == 500) {
                // Update window text with FPS value.
                Text = $"{mName}: {mTimer.FramesPerSecond}fps";
                mFrameCounter = 0;
            }

            mFrameCounter++;

            // Move to the next frame resource.
            //mCurrentFrameResourceIndex = mSwapChain.CurrentBackBufferIndex;
            //mCurrentFrameResourceIndex = Array.IndexOf(mGraphicsDevice.CommandList.RenderTargets, mPresenter.BackBuffer);
            mCurrentFrameResourceIndex = (mCurrentFrameResourceIndex + 1) % FrameCount;
            mCurrentFrameResource = mFrameResources[mCurrentFrameResourceIndex];

            // Make sure that this frame resource isn't still in use by the GPU.
            // If it is, wait for it to complete, because resources still scheduled for GPU execution
            // cannot be modified or else undefined behavior will result.
            mCurrentFrameResource.WaitForSignal(mGraphicsDevice.DirectCommandQueue);

            //UpdateViewProjectionMatrices();
            var model = Matrix4x4.CreateTranslation(0.0f, 0.0f, 0.0f);
            var view = CreateLookTo(new Vector3(8, 8, 30), -Vector3.UnitZ, Vector3.UnitY);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(0.8f, mAspectRatio, 1.0f, 1000.0f);
            Matrix4x4 mvp = model * view * projection;

            // Copy this matrix into the appropriate location in the upload heap subresource.
            mConstantBufferViews[0].Resource.SetData(mvp, (uint)((0) * mConstantBufferViews[0].Resource.SizeInBytes));
        }

        /// <summary>
        /// Render the scene
        /// </summary>
        public virtual void OnRender() {
            var frameIndex = Array.IndexOf(mGraphicsDevice.CommandList.RenderTargets, mPresenter.BackBuffer);
            using (var pe = mCurrentFrameResource.CreateProfilingEvent(mGraphicsDevice.DirectCommandQueue, "Render", frameIndex, mLogger)) {
                // Record all the commands we need to render the scene into the command list.
                CompiledCommandList compiledCommandList = PopulateCommandList(mGraphicsDevice.CommandList, mCurrentFrameResource);

                // Execute the command list.
                mGraphicsDevice.DirectCommandQueue.ExecuteCommandList(compiledCommandList);
            }

            // Present and update the frame index for the next frame.
            mPresenter.Present();

            // Signal and increment the fence value.
            mCurrentFrameResource.AddSignal(mGraphicsDevice.DirectCommandQueue);
        }

        public virtual void OnDestroy() {
            // Ensure that the GPU is no longer referencing resources that are about to be
            // cleaned up by the destructor.
            {
                // Add signal to the command queue.
                var fence = mGraphicsDevice.DirectCommandQueue.AddSignal();

                // Wait until the previous frame is finished.
                mGraphicsDevice.DirectCommandQueue.WaitForSignal(fence);
            }

            mViewProjectionTransformBuffer.Dispose();

            mCbvUploadHeap.Unmap(0);

            foreach (var frameResource in mFrameResources) {
                frameResource.Dispose();
            }

            mPresenter.Dispose();

            mGraphicsDevice.Dispose();

#if DEBUG
            if (DXGI.DXGIGetDebugInterface1(out Vortice.DXGI.Debug.IDXGIDebug1? dxgiDebug).Success) {
                dxgiDebug!.ReportLiveObjects(DXGI.DebugAll, Vortice.DXGI.Debug.ReportLiveObjectFlags.Summary | Vortice.DXGI.Debug.ReportLiveObjectFlags.IgnoreInternal);
                dxgiDebug.Dispose();
            }
#endif
        }

        Matrix4x4 CreateLookTo(Vector3 eyePosition, Vector3 eyeDirection, Vector3 upDirection) {
            Vector3 zaxis = Vector3.Normalize(-eyeDirection);
            Vector3 xaxis = Vector3.Normalize(Vector3.Cross(upDirection, zaxis));
            Vector3 yaxis = Vector3.Cross(zaxis, xaxis);

            Matrix4x4 result = Matrix4x4.Identity;

            result.M11 = xaxis.X;
            result.M12 = yaxis.X;
            result.M13 = zaxis.X;

            result.M21 = xaxis.Y;
            result.M22 = yaxis.Y;
            result.M23 = zaxis.Y;

            result.M31 = xaxis.Z;
            result.M32 = yaxis.Z;
            result.M33 = zaxis.Z;

            result.M41 = -Vector3.Dot(xaxis, eyePosition);
            result.M42 = -Vector3.Dot(yaxis, eyePosition);
            result.M43 = -Vector3.Dot(zaxis, eyePosition);

            return result;
        }

        /// <summary>
        /// Load the rendering pipeline dependencies.
        /// </summary>
        void LoadPipeline() {
            bool dxgiFactoryDebugMode = false; //int dxgiFactoryFlags = 0;

#if DEBUG
            // Enable the debug layer (requires the Graphics Tools "optional feature").
            // NOTE: Enabling the debug layer after device creation will invalidate the active device.
            // NOTE: Ensure native debugging is enabled to see the debug messages from this native code in the Visual Studio Debug window!
            {
                if (D3D12.D3D12GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12Debug? debugController).Success) {
                    debugController!.EnableDebugLayer();
                    debugController.Dispose();

                    // Enable additional debug layers.
                    dxgiFactoryDebugMode = true; //dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG; //0x01
                }
                else {
                    mLogger.Warning("WARNING: Direct3D Debug Device is not available");
                }

                if (DXGI.DXGIGetDebugInterface1(out Vortice.DXGI.Debug.IDXGIInfoQueue? dxgiInfoQueue).Success) {
                    dxgiInfoQueue!.SetBreakOnSeverity(DXGI.DebugAll, Vortice.DXGI.Debug.InfoQueueMessageSeverity.Corruption, true);
                    dxgiInfoQueue.SetBreakOnSeverity(DXGI.DebugAll, Vortice.DXGI.Debug.InfoQueueMessageSeverity.Error, true);

                    var hide = new int[] {
                        80 /* IDXGISwapChain::GetContainingOutput: The swapchain's adapter does not control the output on which the swapchain's window resides. */,
                    };
                    var filter = new Vortice.DXGI.Debug.InfoQueueFilter {
                        DenyList = new Vortice.DXGI.Debug.InfoQueueFilterDescription {
                            Ids = hide
                        }
                    };
                    dxgiInfoQueue.AddStorageFilterEntries(DXGI.DebugDxgi, filter);
                    dxgiInfoQueue.Dispose();
                }
            }
#endif

            DXGI.CreateDXGIFactory2(dxgiFactoryDebugMode, out IDXGIFactory4? factory).CheckError();

            ID3D12Device device;
            if (mUseWarpDevice) {
                Result warpResult = factory!.EnumWarpAdapter(out IDXGIAdapter? warpAdapter);
                if (warpResult.Failure)
                    throw new COMException("EnumWarpAdaptor creation failed", warpResult.Code);

                device = D3D12.D3D12CreateDevice<ID3D12Device>(warpAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }
            else {
                IDXGIAdapter1? hardwareAdapter = null;
                //We could pull this from https://github.com/microsoft/DirectX-Graphics-Samples/blob/3e8b39eba5facbaa3cd26d4196452987ac34499d/Samples/UWP/D3D12HelloWorld/src/HelloTriangle/DXSample.cpp#L43
                //But for now, leave it up to Vortice to figure out...
                //GetHardwareAdapter(factory.Get(), &hardwareAdapter);  

                device = D3D12.D3D12CreateDevice<ID3D12Device>(hardwareAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }

#if DEBUG
            // Configure debug device (if active).
            {
                using Vortice.Direct3D12.Debug.ID3D12InfoQueue? d3dInfoQueue = device.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
                if (d3dInfoQueue != null) {
                    d3dInfoQueue.SetBreakOnSeverity(Vortice.Direct3D12.Debug.MessageSeverity.Corruption, true);
                    d3dInfoQueue.SetBreakOnSeverity(Vortice.Direct3D12.Debug.MessageSeverity.Error, true);
                    var hide = new Vortice.Direct3D12.Debug.MessageId[] {
                        Vortice.Direct3D12.Debug.MessageId.MapInvalidNullRange,
                        Vortice.Direct3D12.Debug.MessageId.UnmapInvalidNullRange,
                        // Workarounds for debug layer issues on hybrid-graphics systems
                        Vortice.Direct3D12.Debug.MessageId.ExecuteCommandListsWrongSwapChainBufferReference,
                        Vortice.Direct3D12.Debug.MessageId.ResourceBarrierMismatchingCommandListType,
                    };

                    var filter = new Vortice.Direct3D12.Debug.InfoQueueFilter {
                        DenyList = new Vortice.Direct3D12.Debug.InfoQueueFilterDescription {
                            Ids = hide
                        }
                    };
                    d3dInfoQueue.AddStorageFilterEntries(filter);

                    d3dInfoQueue.AddMessage(Vortice.Direct3D12.Debug.MessageCategory.Miscellaneous, Vortice.Direct3D12.Debug.MessageSeverity.Warning, Vortice.Direct3D12.Debug.MessageId.SamplePositionsMismatchRecordTimeAssumedFromClear, $"Hi, from {mName}");
                    d3dInfoQueue.AddApplicationMessage(Vortice.Direct3D12.Debug.MessageSeverity.Warning, $"Hi, from application {mName}");
                }
            }
#endif

            // Create the GraphicsDevice abstraction, that also gives us Direct, Compute, and Copy queues, a bunch of descriptor allocators,
            // and a Direct CommandList (which provides its own command allocator).
            mGraphicsDevice = new GraphicsDevice(device, mLogger);

            // Describe and create the swap chain, which also creates descriptor heaps for render target views and the depth stencil view,
            // and the render target views (RTVs) and depth stencil view (DSV) themselves.
            var presentationParameters = new PresentationParameters(Width, Height, Format.R8G8B8A8_UNorm) {
                DepthStencilFormat = Format.D32_Float,
            };
            mPresenter = new HwndSwapChainGraphicsPresenter(factory!, mGraphicsDevice, FrameCount, presentationParameters, base.Handle);
            // Create frame resources.
            {
                // Initialize each frame resource.
                for (var i = 0; i < mFrameResources.Length; i++) {
                    mFrameResources[i] = new FrameResource(mGraphicsDevice, $"{nameof(mFrameResources)}[{i}]");

                }
            }
        }

        /// <summary>
        /// Load the sample assets
        /// </summary>
        void LoadAssets() {
            // Create the root signature.
            {
                var highestVersion = mGraphicsDevice.NativeDevice.CheckHighestRootSignatureVersion(RootSignatureVersion.Version11);
                var sampler = new StaticSamplerDescription {
                    Filter = Filter.MinMagMipPoint,
                    AddressU = TextureAddressMode.Border,
                    AddressV = TextureAddressMode.Border,
                    AddressW = TextureAddressMode.Border,
                    MipLODBias = 0,
                    MaxAnisotropy = 0,
                    ComparisonFunction = ComparisonFunction.Never,
                    BorderColor = StaticBorderColor.TransparentBlack,
                    MinLOD = 0.0f,
                    MaxLOD = 0.0f,
                    ShaderRegister = 0,
                    RegisterSpace = 0,
                    ShaderVisibility = ShaderVisibility.Pixel,
                };
                VersionedRootSignatureDescription rootSignatureDesc;
                switch (highestVersion) {
                    case RootSignatureVersion.Version11:
                        var range111 = new DescriptorRange1(DescriptorRangeType.ConstantBufferView, 1, 0, 0, -1, DescriptorRangeFlags.DataStatic);
                        var range112 = new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1, DescriptorRangeFlags.DataStatic);
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription1 {
                            Parameters = new[] {
                                new RootParameter1(new RootDescriptorTable1(range111), ShaderVisibility.All),
                                new RootParameter1(new RootDescriptorTable1(range112), ShaderVisibility.Pixel),
                            },

                            StaticSamplers = new[] { sampler },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;

                    case RootSignatureVersion.Version10:
                    default:
                        var range101 = new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 0, 0, -1);
                        var range102 = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1);
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription {
                            Parameters = new[] {
                                new RootParameter(new RootDescriptorTable(range101), ShaderVisibility.All),
                                new RootParameter(new RootDescriptorTable(range102), ShaderVisibility.Pixel),
                            },
                            StaticSamplers = new[] { sampler },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;
                }
                mRootSignature = mGraphicsDevice.NativeDevice.CreateRootSignature(0, rootSignatureDesc);
                mRootSignature.Name = nameof(mRootSignature);
            }

            // Create the pipeline state, which includes compiling and loading shaders.
            {
                // Compile .NET to HLSL
                var shader = new Shaders();
                var shaderGenerator = new DirectX12GameEngine.Shaders.ShaderGenerator(shader);
                DirectX12GameEngine.Shaders.ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                byte[] vertexShader = DirectX12GameEngine.Shaders.ShaderCompiler.Compile(DirectX12GameEngine.Shaders.ShaderStage.VertexShader, result.ShaderSource, nameof(shader.VSMain));
                byte[] pixelShader = DirectX12GameEngine.Shaders.ShaderCompiler.Compile(DirectX12GameEngine.Shaders.ShaderStage.PixelShader, result.ShaderSource, nameof(shader.PSMain));

                // Describe and create the graphics pipeline state object (PSO).
                mPipelineState = new PipelineState(mGraphicsDevice, mRootSignature, FlatShadedVertex.InputElements, vertexShader, pixelShader, name: nameof(mPipelineState));
            }

            // Reset the command list, we need it open for initial GPU setup.
            mGraphicsDevice.CommandList.Reset();

            // Load the ship buffers (this one uses copy command queue, so need to create a fence for it first, so we can wait for it to finish)
            {
                var modelLoader = XModelLoader.Create3(mGraphicsDevice, @"..\..\..\Mutiny\Models\cannon_boss.X");
                (ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, IEnumerable<ShaderResourceView> ShaderResourceViews, Model Model) firstMesh
                    = System.Threading.Tasks.Task.Run(() => modelLoader.GetFlatShadedMeshesAsync(@"..\..\..\Mutiny", false)).Result.First();
                mVertexBuffer = firstMesh.VertexBuffer;
                mVertexBuffer.Name = nameof(mVertexBuffer);

                mIndexBuffer = firstMesh.IndexBuffer;
                mIndexBuffer.Name = nameof(mIndexBuffer);

                mModel = firstMesh.Model;
                mShaderResourceViews = firstMesh.ShaderResourceViews;

                //mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mShaderResourceViews.First().Resource.NativeResource, ResourceStates.Common, ResourceStates.PixelShaderResource));
                //Upload texture data to the GPU
                //TODO:
                //CpuDescriptorHandle destinationDescriptor = mSrvHeap!.Allocate(1);
                //mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mModel.Materials.First().ShaderResourceViewDescriptorSet!.StartCpuDescriptorHandle, mSrvHeap.DescriptorHeap.Description.Type);
                //GpuDescriptorHandle gpuDescriptor = mSrvHeap.GetGpuDescriptorHandle(destinationDescriptor);
                //mGraphicsDevice.CopyCommandQueue.ExecuteCommandList

            }

            // Create Constant Buffer Views
            {
                // Create an upload heap for the constant buffers.
                mCbvUploadHeap = GraphicsResource.CreateBuffer(mGraphicsDevice, (Unsafe.SizeOf<Matrix4x4>() + 255) & ~255,
                                                               ResourceFlags.None, HeapType.Upload);
                mCbvUploadHeap.NativeResource.Name = nameof(mCbvUploadHeap);

                // Map the constant buffers. Note that unlike D3D11, the resource 
                // does not need to be unmapped for use by the GPU. In this sample, 
                // the resource stays 'permanently' mapped to avoid overhead with 
                // mapping/unmapping each frame.
                mCbvUploadHeap.Map(0);

                // Create CBVs
                var constantBufferViews = new ConstantBufferView[1];
                var constantBufferSize = mCbvUploadHeap.SizeInBytes;
                ulong cbOffset = 0;
                constantBufferViews[0] = new ConstantBufferView(mCbvUploadHeap, cbOffset, constantBufferSize, mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator);
                // Increment the offset for the next one
                cbOffset += constantBufferSize;
                mConstantBufferViews = constantBufferViews;


                mViewProjectionTransformBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            }

            // Close the command list and execute it to begin the initial GPU setup.
            // This higher level abstraction will also wait until the command list finishes execution,
            // which means the assets have been uploaded to the GPU before we continue.
            mGraphicsDevice.DirectCommandQueue.ExecuteCommandLists(mGraphicsDevice.CommandList.Close());
        }

        CompiledCommandList PopulateCommandList(CommandList commandList, FrameResource frameResource) {
            commandList.Reset(frameResource.CommandAllocator);

            // Set necessary state.
            commandList.SetRootSignature(mPipelineState);
            commandList.SetShaderVisibleDescriptorHeaps();

            commandList.SetViewports(mViewport);
            commandList.SetScissorRectangles(mScissorRect);

            // Indicate that the back buffer will be used as a render target.
            commandList.ResourceBarrierTransition(mPresenter.BackBuffer.Resource, ResourceStates.Present, ResourceStates.RenderTarget);

            //Ultimately calls OMSetRenderTargets after updating CommandList.RenderTargets accordingly, however as not setting all render targets, can't use this since
            //presenter doesn't expose all of them publicly.
            //commandList.SetRenderTargets(commandList.DepthStencilBuffer, commandList.RenderTargets);
            commandList.OMSetRenderTargets(mPresenter.BackBuffer.CpuDescriptorHandle, commandList.DepthStencilBuffer!.CpuDescriptorHandle);

            // Record commands.
            var clearColor = new Color4(0.0f, 0.2f, 0.4f, 1.0f);
            commandList.ClearRenderTargetView(mPresenter.BackBuffer, clearColor);

            var meshCount = mModel.Meshes.Count;
            var worldMatrixBuffers = new GraphicsResource[meshCount];
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++) {
                worldMatrixBuffers[meshIndex] = GraphicsResource.CreateBuffer(mGraphicsDevice, 1 * Unsafe.SizeOf<Matrix4x4>(), ResourceFlags.None, HeapType.Upload);

                //If using bundles, this would be done after recording the commands
                worldMatrixBuffers[meshIndex].SetData(mModel.Meshes[meshIndex].WorldMatrix, 0);
            }

            RecordCommandList(mModel, commandList, worldMatrixBuffers, 1);

            // Indicate that the back buffer will now be used to present.
            commandList.ResourceBarrierTransition(mPresenter.BackBuffer.Resource, ResourceStates.RenderTarget, ResourceStates.Present);

            return commandList.Close();
        }

        private void RecordCommandList(Model model, CommandList commandList, GraphicsResource[] worldMatrixBuffers, int instanceCount) {
            int renderTargetCount = 1; //We don't do stereo so this is always 1
            instanceCount *= renderTargetCount;

            for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++) {
                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[mesh.MaterialIndex];

                commandList.SetPipelineState(material.PipelineState!, true);
                commandList.SetShaderVisibleDescriptorHeaps();

                commandList.SetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
                commandList.SetIndexBuffer(mesh.MeshDraw.IndexBufferView);
                commandList.SetVertexBuffers(0, mesh.MeshDraw.VertexBufferViews!);


                int rootParameterIndex = 0;
                commandList.SetGraphicsRootConstantBufferViewGpuBound(rootParameterIndex++, mConstantBufferViews[0]);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mViewProjectionTransformBuffer.DefaultConstantBufferView);

                //commandList.SetGraphicsRoot32BitConstant(rootParameterIndex++, renderTargetCount, 0);

                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mGlobalBuffer.DefaultConstantBufferView);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mViewProjectionTransformBuffer.DefaultConstantBufferView);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, worldMatrixBuffers[meshIndex].DefaultConstantBufferView);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mDirectionalLightGroupBuffer.DefaultConstantBufferView);
                //commandList.SetGraphicsRootSampler(rootParameterIndex++, mDefaultSampler);
                if (material.ShaderResourceViewDescriptorSet != null) {
                    commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, material.ShaderResourceViewDescriptorSet);
                }

                //if (material.SamplerDescriptorSet != null) {
                //    commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, material.SamplerDescriptorSet);
                //}

                if (mesh.MeshDraw.IndexBufferView != null) {
                    commandList.DrawIndexedInstanced(mesh.MeshDraw.IndexBufferView.Value.SizeInBytes / (mesh.MeshDraw.IndexBufferView.Value.Format.GetBitsPerPixel() >> 3), instanceCount);
                }
                else {
                    var firstVertexBufferView = mesh.MeshDraw.VertexBufferViews!.First();
                    commandList.DrawInstanced(firstVertexBufferView.SizeInBytes / firstVertexBufferView.StrideInBytes, instanceCount);
                }
            }
        }

    }
}
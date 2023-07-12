using D3D12HelloWorld.Rendering;
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
using Vortice.Dxc;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace D3D12HelloWorld.Mutiny
{
    public partial class D3D12Mutiny : Form {
        const int FrameCount = 2;

        readonly struct Vertex {
            public static readonly unsafe int SizeInBytes = sizeof(Vertex);
            /// <summary>
            /// Defines the vertex input layout.
            /// NOTE: The HLSL Semantic names here must match the ShaderTypeAttribute.TypeName associated with the ShaderSemanticAttribute associated with the 
            ///       compiled Vertex Shader's Input parameters - PositionSemanticAttribute and ColorSemanticAttribute in this case per the VSInput struct
            /// </summary>
            public static readonly InputElementDescription[] InputElements = new[] {
                new InputElementDescription("Position", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("Color", 0, Format.R32G32B32A32_Float, 12, 0, InputClassification.PerVertexData, 0),
            };

            public Vertex(in Vector3 position, in Color4 colour) {
                Position = position;
                Colour = colour;
            }

            public readonly Vector3 Position;
            public readonly Color4 Colour;
        };

        //Viewport dimensions
        float mAspectRatio;

        //Adapter info
        bool mUseWarpDevice;

        // Pipeline objects.
        Viewport mViewport;
        RawRect mScissorRect;
        IDXGISwapChain3 mSwapChain;
        ID3D12Device mDevice;
        readonly ID3D12Resource[] mRenderTargets;
        readonly ID3D12CommandAllocator[] mCommandAllocators;
        ID3D12CommandQueue mCommandQueue;
        ID3D12RootSignature mRootSignature;
        ID3D12DescriptorHeap mRtvHeap;
        ID3D12PipelineState mPipelineState;  //Potentially replaceable by mGraphicsDevice
        ID3D12GraphicsCommandList mCommandList;  //Potentially replaceable by mGraphicsDevice
        GraphicsDevice mGraphicsDevice;
        int mRtvDescriptorSize;

        // App resources.
        GraphicsResource mDirectionalLightGroupBuffer;
        GraphicsResource mGlobalBuffer;
        GraphicsResource mViewProjectionTransformBuffer;
        Sampler mDefaultSampler;
        ID3D12Resource mIndexBuffer;
        ID3D12Resource mVertexBuffer;
        Model mModel;

        // Synchronization objects.
        int mFrameIndex;
        ManualResetEvent mFenceEvent;
        ID3D12Fence mFence;
        readonly ulong[] mFenceValues;

        //GameBase
        private readonly object mTickLock = new object();

        public D3D12Mutiny() : this(1200, 900, string.Empty) {
        }

        public D3D12Mutiny(uint width, uint height, string name) {
            InitializeComponent();

            Width = Convert.ToInt32(width);
            Height = Convert.ToInt32(height);
            if (!string.IsNullOrEmpty(name))
                Text = name;

            mAspectRatio = width / (float)height;

            mViewport = new Viewport(width, height);
            mScissorRect = new RawRect(0, 0, Width, Height);
            mRenderTargets = new ID3D12Resource[FrameCount];
            mCommandAllocators = new ID3D12CommandAllocator[FrameCount];
            mFenceValues = new ulong[FrameCount];

            OnInit();

            CompositionTarget.Rendering += HandleCompositionTarget_Rendering;
            this.FormClosing += (object? sender, FormClosingEventArgs e) => OnDestroy();
        }

        private void HandleCompositionTarget_Rendering(object sender, EventArgs e) {
            lock (mTickLock) {
                OnUpdate();

                OnRender();
            }
        }

        public virtual void OnInit() {
            LoadPipeline();
            LoadAssets();

            //mDirectionalLightGroupBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, sizeof(int) + System.Runtime.CompilerServices.Unsafe.SizeOf<DirectionalLightData>() * MaxLights, ResourceFlags.None, HeapType.Upload);
            //mGlobalBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, System.Runtime.CompilerServices.Unsafe.SizeOf<GlobalBuffer>(), ResourceFlags.None, HeapType.Upload);
            //mViewProjectionTransformBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, System.Runtime.CompilerServices.Unsafe.SizeOf<StereoViewProjectionTransform>(), ResourceFlags.None, HeapType.Upload);
            mDirectionalLightGroupBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            mGlobalBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            mViewProjectionTransformBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            mDefaultSampler = new Sampler(mDevice);
        }

        /// <summary>
        /// Update frame-based values
        /// </summary>
        public virtual void OnUpdate() {
        }

        /// <summary>
        /// Render the scene
        /// </summary>
        public virtual void OnRender() {
            //UpdateViewProjectionMatrices();
            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Populating Direct Command list. Frame index: {mFrameIndex} with fence value {fenceValue}",
                             nameof(OnRender), mFrameIndex, mFenceValues[mFrameIndex]);

            // Record all the commands we need to render the scene into the command list.
            PopulateCommandList();

            // Execute the command list.
            mCommandQueue.ExecuteCommandList(mCommandList);
            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Direct Command list submitted to GPU for execution. Frame index: {mFrameIndex} with fence value {fenceValue}",
                             nameof(OnRender), mFrameIndex, mFenceValues[mFrameIndex]);

            // Present the frame.
            var presentResult = mSwapChain.Present(1, PresentFlags.None);
            if (presentResult.Failure) {
                if (presentResult.Code == Vortice.DXGI.ResultCode.DeviceRemoved.Code) {
                    //Lookup code at https://docs.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-error
                    //Application should destroy and recreate the device (and all resources)
                    //I encountered DXGI_ERROR_INVALID_CALL... 0x887A0001
                    throw new COMException($"DXGI_ERROR_DEVICE_REMOVED, GetDeviceRemovedReason() yielded 0x{mDevice.DeviceRemovedReason.Code:X} {mDevice.DeviceRemovedReason.Description}.  During frame index {mFrameIndex} with fence value {mFenceValues[mFrameIndex]}", presentResult.Code);
                }
                else {
                    throw new COMException($"SwapChain.Present failed 0x{presentResult.Code:X} during frame index {mFrameIndex} with fence value {mFenceValues[mFrameIndex]}.", presentResult.Code);
                }
            }

            MoveToNextFrame();
        }

        public virtual void OnDestroy() {
            // Ensure that the GPU is no longer referencing resources that are about to be
            // cleaned up by the destructor.
            WaitForGpu();

            for (int i = 0; i < mCommandAllocators.Length; i++) {
                mCommandAllocators[i].Dispose();
                mRenderTargets[i].Dispose();
            }
            mCommandList.Dispose();
            mGraphicsDevice.Dispose();

            mFenceEvent.Dispose();
            //Replacing CloseHandle(mFenceEvent);

            mRtvHeap.Dispose();
            mSwapChain.Dispose();
            mCommandQueue.Dispose();

            mDevice.Dispose();
            //mFactory.Dispose();

#if DEBUG
            if (DXGI.DXGIGetDebugInterface1(out Vortice.DXGI.Debug.IDXGIDebug1? dxgiDebug).Success) {
                dxgiDebug!.ReportLiveObjects(DXGI.DebugAll, Vortice.DXGI.Debug.ReportLiveObjectFlags.Summary | Vortice.DXGI.Debug.ReportLiveObjectFlags.IgnoreInternal);
                dxgiDebug.Dispose();
            }
#endif
        }

        /// <summary>
        /// Load the rendering pipeline dependencies.
        /// </summary>
        void LoadPipeline() {
            bool dxgiFactoryDebugMode = false; //int dxgiFactoryFlags = 0;

#if DEBUG
            // Enable the debug layer (requires the Graphics Tools "optional feature").
            // NOTE: Enabling the debug layer after device creation will invalidate the active device.
            {
                if (D3D12.D3D12GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12Debug? debugController).Success) {
                    //Doesn't implement this interface....
                    //ID3D12InfoQueue debug = debugController.QueryInterface<ID3D12InfoQueue>();
                    //if (debug != null) {
                    //    debug.AddMessage(MessageCategory.Miscellaneous, MessageSeverity.Warning, MessageId.SamplePositionsMismatchRecordTimeAssumedFromClear, "Hi from Sam");
                    //    debug.AddApplicationMessage(MessageSeverity.Warning, "Hi from Application");
                    //}                    

                    //ID3D12Debug1 debug = debugController.QueryInterface<ID3D12Debug1>();
                    //debug.EnableDebugLayer();
                    debugController!.EnableDebugLayer();
                    debugController.Dispose();

                    // Enable additional debug layers.
                    dxgiFactoryDebugMode = true; //dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG; //0x01
                }
                else {
                    System.Diagnostics.Debug.WriteLine("WARNING: Direct3D Debug Device is not available");
                }

                if (DXGI.DXGIGetDebugInterface1(out Vortice.DXGI.Debug.IDXGIInfoQueue? dxgiInfoQueue).Success) {
                    dxgiInfoQueue!.SetBreakOnSeverity(DXGI.DebugAll, Vortice.DXGI.Debug.InfoQueueMessageSeverity.Error, true);
                    dxgiInfoQueue.SetBreakOnSeverity(DXGI.DebugAll, Vortice.DXGI.Debug.InfoQueueMessageSeverity.Corruption, true);

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

            if (mUseWarpDevice) {
                Result warpResult = factory!.EnumWarpAdapter(out IDXGIAdapter? warpAdapter);
                if (warpResult.Failure)
                    throw new COMException("EnumWarpAdaptor creation failed", warpResult.Code);

                mDevice = D3D12.D3D12CreateDevice<ID3D12Device>(warpAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }
            else {
                IDXGIAdapter1? hardwareAdapter = null;
                //We could pull this from https://github.com/microsoft/DirectX-Graphics-Samples/blob/3e8b39eba5facbaa3cd26d4196452987ac34499d/Samples/UWP/D3D12HelloWorld/src/HelloTriangle/DXSample.cpp#L43
                //But for now, leave it up to Vortice to figure out...
                //GetHardwareAdapter(factory.Get(), &hardwareAdapter);  

                mDevice = D3D12.D3D12CreateDevice<ID3D12Device>(hardwareAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }

#if DEBUG
            // Configure debug device (if active).
            {
                using Vortice.Direct3D12.Debug.ID3D12InfoQueue? d3dInfoQueue = mDevice.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
                if (d3dInfoQueue != null) {
                    d3dInfoQueue!.SetBreakOnSeverity(Vortice.Direct3D12.Debug.MessageSeverity.Corruption, true);
                    d3dInfoQueue!.SetBreakOnSeverity(Vortice.Direct3D12.Debug.MessageSeverity.Error, true);
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
                }
            }
#endif

            //12_1
            //var featureLevel = mDevice.CheckMaxSupportedFeatureLevel();

            // Describe and create the command queue.
            mCommandQueue = mDevice.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
            mCommandQueue.Name = "Direct Queue";

            // Describe and create the swap chain.
            var swapChainDesc = new SwapChainDescription1 {
                BufferCount = FrameCount,
                Width = Width,
                Height = Height,
                Format = Format.R8G8B8A8_UNorm,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = new SampleDescription(1, 0),
                //AlphaMode = AlphaMode.Ignore,
            };

            // Swap chain needs the queue so that it can force a flush on it.
            using IDXGISwapChain1 swapChain = factory!.CreateSwapChainForHwnd(mCommandQueue, base.Handle, swapChainDesc);
            //factory.MakeWindowAssociation(base.Handle, WindowAssociationFlags.IgnoreAltEnter);
            mSwapChain = swapChain.QueryInterface<IDXGISwapChain3>();
            mFrameIndex = mSwapChain.CurrentBackBufferIndex;

            // Create descriptor heaps.
            {
                // Describe and create a render target view (RTV) descriptor heap.
                var rtvHeapDesc = new DescriptorHeapDescription {
                    DescriptorCount = FrameCount,
                    Type = DescriptorHeapType.RenderTargetView,
                    Flags = DescriptorHeapFlags.None,
                };
                mRtvHeap = mDevice.CreateDescriptorHeap(rtvHeapDesc);

                mRtvDescriptorSize = mDevice.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);  //Or mRtvHeap.Description.Type
            }

            // Create frame resources.
            {
                CpuDescriptorHandle rtvHandle = mRtvHeap.GetCPUDescriptorHandleForHeapStart();

                // Create a RTV and a command allocator for each frame.
                for (int n = 0; n < swapChainDesc.BufferCount; n++) {
                    mRenderTargets[n] = mSwapChain.GetBuffer<ID3D12Resource>(n);

                    mDevice.CreateRenderTargetView(mRenderTargets[n], null, rtvHandle);
                    rtvHandle += 1 * mRtvDescriptorSize;

                    mCommandAllocators[n] = mDevice.CreateCommandAllocator(CommandListType.Direct);
                    mCommandAllocators[n].Name = $"Direct Allocator {n}";
                }
            }
        }

        /// <summary>
        /// Load the sample assets
        /// </summary>
        void LoadAssets() {
            {
                var rootSignatureDesc = new RootSignatureDescription1 {
                    Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                };
                //mRootSignature = mDevice.CreateRootSignature(0, new VersionedRootSignatureDescription(rootSignatureDesc));
                mRootSignature = mDevice.CreateRootSignature(rootSignatureDesc);
            }

            // Create the pipeline state, which includes compiling and loading shaders.
            {
                //var shader = new HelloTriangle.Shaders();
                //var shaderGenerator = new ShaderGenerator(shader);
                //ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                //ReadOnlyMemory<byte> vertexShader = ShaderCompiler.Compile(ShaderStage.VertexShader, result.ShaderSource, nameof(shader.VSMain));
                //ReadOnlyMemory<byte> pixelShader = ShaderCompiler.Compile(ShaderStage.PixelShader, result.ShaderSource, nameof(shader.PSMain));
                ReadOnlyMemory<byte> vertexShader = CompileBytecode(DxcShaderStage.Vertex, "HelloTriangleShaders.hlsl", "VSMain");
                ReadOnlyMemory<byte> pixelShader = CompileBytecode(DxcShaderStage.Pixel, "HelloTriangleShaders.hlsl", "PSMain");

                // Describe and create the graphics pipeline state object (PSO).
                var psoDesc = new GraphicsPipelineStateDescription {
                    InputLayout = new InputLayoutDescription(Vertex.InputElements),
                    RootSignature = mRootSignature,
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                    RasterizerState = RasterizerDescription.CullNone, //I think this corresponds to CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT)
                    //BlendState = BlendDescription.AlphaBlend,  //This is what DX12GE uses
                    //BlendState = BlendDescription.Opaque,  //Nothing seems to correspond to CD3DX12_BLEND_DESC(D3D12_DEFAULT)
                    BlendState = BlendDescription.NonPremultiplied, //i.e. new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha),
                    //DepthStencilState = DepthStencilDescription.None,  //Default value is DepthStencilDescription.Default
                    SampleMask = uint.MaxValue, //This is the default value anyway...
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                    RenderTargetFormats = new[] { Format.R8G8B8A8_UNorm, },
                    SampleDescription = SampleDescription.Default,  //This is the default value anyway...
                };
                mPipelineState = mDevice.CreateGraphicsPipelineState(psoDesc);
            }

            // Create the command list.
            mCommandList = mDevice.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, mCommandAllocators[mFrameIndex], mPipelineState);
            mCommandList.Name = "Direct Command List";

            // Command lists are created in the recording state, but there is nothing
            // to record yet. The main loop expects it to be closed, so close it now.
            mCommandList.Close();

            //Rather than just a command list, use the GraphicsDevice abstraction which creates both the CommandList
            mGraphicsDevice = new GraphicsDevice(mDevice);

            //// Create the vertex buffer.
            //{
            //    // Define the geometry for a triangle.
            //    var triangleVertices = new[] {
            //        new Vertex(new Vector3(0.0f, 0.25f * mAspectRatio, 0.0f), new Color4(1.0f, 0.0f, 0.0f, 1.0f)),
            //        new Vertex(new Vector3(0.25f, -0.25f * mAspectRatio, 0.0f), new Color4(0.0f, 1.0f, 0.0f, 1.0f)),
            //        new Vertex(new Vector3(-0.25f, -0.25f * mAspectRatio, 0.0f), new Color4(0.0f, 0.0f, 1.0f, 1.0f)),
            //    };

            //    int vertexBufferSize = triangleVertices.Length * Vertex.SizeInBytes;

            //    // Note: using upload heaps to transfer static data like vert buffers is not 
            //    // recommended. Every time the GPU needs it, the upload heap will be marshalled 
            //    // over. Please read up on Default Heap usage. An upload heap is used here for 
            //    // code simplicity and because there are very few verts to actually transfer.
            //    mVertexBuffer = mDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
            //                                                    ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead, null);

            //    // Copy the triangle data to the vertex buffer.
            //    mVertexBuffer.SetData(triangleVertices);

            //    // Initialize the vertex buffer view.
            //    mVertexBufferView.BufferLocation = mVertexBuffer.GPUVirtualAddress;
            //    mVertexBufferView.StrideInBytes = Vertex.SizeInBytes;
            //    mVertexBufferView.SizeInBytes = vertexBufferSize;
            //}

            // Load the ship buffers (this one uses copy command queue, so need to create a fence for it first, so we can wait for it to finish)
            {
                var modelLoader = XModelLoader.Create3(mGraphicsDevice, @"..\..\..\Mutiny\Models\cannon_boss.X");
                (ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, Model Model) firstMesh
                    = System.Threading.Tasks.Task.Run(() => modelLoader.GetFlatShadedMeshesAsync(@"..\..\..\Mutiny", false)).Result.First();
                mIndexBuffer = firstMesh.IndexBuffer;
                mVertexBuffer = firstMesh.VertexBuffer;
                mModel = firstMesh.Model;
            }

            // Create synchronization objects and wait until assets have been uploaded to the GPU.
            {
                mFence = mDevice.CreateFence(mFenceValues[mFrameIndex], FenceFlags.None);
                mFence.Name = "Frame Fence";
                mFenceValues[mFrameIndex]++;

                // Create an event handle to use for frame synchronization.
                mFenceEvent = new ManualResetEvent(false);

                // Wait for the command list to execute; we are reusing the same command 
                // list in our main loop but for now, we just want to wait for setup to 
                // complete before continuing.
                WaitForGpu();
            }
        }

        void PopulateCommandList()
        {
            var descriptorHeaps = new[] { mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator, mGraphicsDevice.ShaderVisibleSamplerAllocator };
            var srvDescriptorHeap = descriptorHeaps.SingleOrDefault(d => d.DescriptorHeap.Description.Type == DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            var samplerDescriptorHeap = descriptorHeaps.SingleOrDefault(d => d.DescriptorHeap.Description.Type == DescriptorHeapType.Sampler);


            // Command list allocators can only be reset when the associated 
            // command lists have finished execution on the GPU; apps should use 
            // fences to determine GPU execution progress.
            Log.Logger.Write(Serilog.Events.LogEventLevel.Verbose, "Resetting command allocator for frame {mFrameIndex}", mFrameIndex);
            var commandAllocator = mCommandAllocators[mFrameIndex];
            commandAllocator.Reset();

            // However, when ExecuteCommandList() is called on a particular command 
            // list, that command list can then be reset at any time and must be before 
            // re-recording.
            Log.Logger.Write(Serilog.Events.LogEventLevel.Verbose, "Resetting command list for frame {mFrameIndex}", mFrameIndex);
            mCommandList.Reset(commandAllocator, mPipelineState);

            // Set necessary state.
            mCommandList.SetDescriptorHeaps(descriptorHeaps.Length, descriptorHeaps.Select(d => d.DescriptorHeap).ToArray());  //Performed by CommandList.Reset
            //mCommandList.SetGraphicsRootSignature(mRootSignature);  //Not set by CommandList, but bny RenderSystem when looping through the meshes, after setting index and vertext buggers, before the primitive topology
            mCommandList.RSSetViewports(mViewport);  //Set during CommandList.ClearState
            mCommandList.RSSetScissorRects(mScissorRect);  //Set during CommandList.ClearState

            // Indicate that the back buffer will be used as a render target.
            var backBufferRenderTarget = mRenderTargets[mFrameIndex];
            mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(backBufferRenderTarget, ResourceStates.Present, ResourceStates.RenderTarget));

            CpuDescriptorHandle rtvHandle = mRtvHeap.GetCPUDescriptorHandleForHeapStart() + mFrameIndex * mRtvDescriptorSize;
            mCommandList.OMSetRenderTargets(rtvHandle, null);  //Set during CommandList.ClearState

            // Record commands.
            var clearColor = new Vortice.Mathematics.Color(0, Convert.ToInt32(0.2f * 255.0f), Convert.ToInt32(0.4f * 255.0f), 255);
            mCommandList.ClearRenderTargetView(rtvHandle, clearColor);  //MyGame.BeginDraw

            var meshCount = mModel.Meshes.Count();
            var worldMatrixBuffers = new GraphicsResource[meshCount];
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                worldMatrixBuffers[meshIndex] = GraphicsResource.CreateBuffer(mGraphicsDevice, 1 * Unsafe.SizeOf<Matrix4x4>(), ResourceFlags.None, HeapType.Upload);

                //If using bundles, this would be done after recording the commands
                worldMatrixBuffers[meshIndex].SetData(mModel.Meshes[meshIndex].WorldMatrix);
            }

            RecordCommandList(mModel, mCommandList, srvDescriptorHeap, samplerDescriptorHeap, worldMatrixBuffers, 1);

            // Indicate that the back buffer will now be used to present.
            mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(backBufferRenderTarget, ResourceStates.RenderTarget, ResourceStates.Present));

            mCommandList.Close();
        }

        private void RecordCommandList(Model model, ID3D12GraphicsCommandList commandList, DescriptorAllocator? srvDescriptorHeap, DescriptorAllocator? samplerDescriptorHeap, GraphicsResource[] worldMatrixBuffers, int instanceCount)
        {
            int renderTargetCount = 1; //We don't do stereo so this is always 1
            instanceCount *= renderTargetCount;

            for (int meshIndex = 0; meshIndex < model.Meshes.Count(); meshIndex++)
            {
                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[mesh.MaterialIndex];

                //commandList.IASetIndexBuffer(mesh.MeshDraw.IndexBufferView);
                commandList.IASetVertexBuffers(0, mesh.MeshDraw.VertexBufferViews);

                //CommandList.SetPipelineState(material.PipelineState) performs:
                if (material.PipelineState!.IsCompute)
                {
                    commandList.SetComputeRootSignature(material.PipelineState.RootSignature);
                }
                else
                {
                    commandList.SetGraphicsRootSignature(material.PipelineState.RootSignature);
                }
                commandList.SetPipelineState(material.PipelineState.NativePipelineState);

                commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

                int rootParameterIndex = 0;
                commandList.SetGraphicsRoot32BitConstant(rootParameterIndex++, renderTargetCount, 0);

                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mGlobalBuffer.DefaultConstantBufferView);
                //long gpuDescriptor = CopyDescriptors(srvDescriptorHeap, mGlobalBuffer.DefaultConstantBufferView.CpuDescriptorHandle, 1);
                CpuDescriptorHandle destinationDescriptor = srvDescriptorHeap!.Allocate(1);
                mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mGlobalBuffer.DefaultConstantBufferView.CpuDescriptorHandle, srvDescriptorHeap.DescriptorHeap.Description.Type);
                GpuDescriptorHandle gpuDescriptor = srvDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);

                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mViewProjectionTransformBuffer.DefaultConstantBufferView);
                //long gpuDescriptor = CopyDescriptors(srvDescriptorHeap, mViewProjectionTransformBuffer.DefaultConstantBufferView.CpuDescriptorHandle, 1);
                destinationDescriptor = srvDescriptorHeap!.Allocate(1);
                mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mViewProjectionTransformBuffer.DefaultConstantBufferView.CpuDescriptorHandle, srvDescriptorHeap.DescriptorHeap.Description.Type);
                gpuDescriptor = srvDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);

                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mWorldMatrixBuffer.DefaultConstantBufferView);
                //long gpuDescriptor = CopyDescriptors(srvDescriptorHeap, worldMatrixBuffers[meshIndex].DefaultConstantBufferView.CpuDescriptorHandle, 1);
                destinationDescriptor = srvDescriptorHeap!.Allocate(1);
                mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, worldMatrixBuffers[meshIndex].DefaultConstantBufferView.CpuDescriptorHandle, srvDescriptorHeap.DescriptorHeap.Description.Type);
                gpuDescriptor = srvDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);

                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mDirectionalLightGroupBuffer.DefaultConstantBufferView);
                //long gpuDescriptor = CopyDescriptors(srvDescriptorHeap, mDirectionalLightGroupBuffer.DefaultConstantBufferView.CpuDescriptorHandle, 1);
                destinationDescriptor = srvDescriptorHeap!.Allocate(1);
                mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mDirectionalLightGroupBuffer.DefaultConstantBufferView.CpuDescriptorHandle, srvDescriptorHeap.DescriptorHeap.Description.Type);
                gpuDescriptor = srvDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);

                //commandList.SetGraphicsRootSampler(rootParameterIndex++, mDefaultSampler);
                //long gpuDescriptor = CopyDescriptors(samplerDescriptorHeap, mDefaultSampler.CpuDescriptorHandle, 1);
                destinationDescriptor = samplerDescriptorHeap!.Allocate(1);
                mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mDefaultSampler.CpuDescriptorHandle, samplerDescriptorHeap.DescriptorHeap.Description.Type);
                gpuDescriptor = samplerDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);

                //(mIndexBufferView.SizeInBytes / mIndexBufferView.Format.SizeOfInBytes() comes out at 11295, rather than 3765...
                //maybe need to take into account the fact it is triangles, divide by three?  I don't see how the DirectX12GameEngine handled this
                //DrawIndexedInstanced(int indexCountPerInstance, int instanceCount, int startIndexLocation, int baseVertexLocation, int startInstanceLocation)
                //2022-12-04 Update, no more SizeOfInBytes method, instead we have GetBitsPerPixel, so rightshift by 3 (or divide by 8)
                //commandList.DrawIndexedInstanced(mIndexBufferView.SizeInBytes / (mIndexBufferView.Format.GetBitsPerPixel() >> 3) / 3, 1, 0, 0, 0);
                //Blank
                //commandList.DrawInstanced(instanceCount * 3, mVertexBufferView.SizeInBytes / mVertexBufferView.StrideInBytes / (instanceCount * 3), 0, 0);
                var firstVertexBufferView = mesh.MeshDraw.VertexBufferViews!.First();
                commandList.DrawInstanced(firstVertexBufferView.SizeInBytes / firstVertexBufferView.StrideInBytes, instanceCount, 0, 0);
            }
        }

        // Wait for pending GPU work to complete.
        void WaitForGpu() {
            ulong requiredFenceValue = mFenceValues[mFrameIndex];
            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName} Signal current fence value {currentFenceValue} for frame {mFrameIndex}",
                             nameof(WaitForGpu), requiredFenceValue, mFrameIndex);

            // Schedule a Signal command in the queue.
            mCommandQueue.Signal(mFence, requiredFenceValue);

            // Wait until the fence has been processed.
            mFenceEvent.Reset();
            if (mFence.SetEventOnCompletion(requiredFenceValue, mFenceEvent).Success) {
                Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Waiting for {requiredFenceValue}, currently at {currentFenceValue}",
                                 nameof(WaitForGpu), requiredFenceValue, mFence.CompletedValue);
                mFenceEvent.WaitOne();

                // Increment the fence value for the current frame.
                mFenceValues[mFrameIndex]++;
                Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Wait complete, current fence value updated to {currentFenceValue} for frame {mFrameIndex}",
                                 nameof(WaitForGpu), mFenceValues[mFrameIndex], mFrameIndex);
            }
        }

        // Prepare to render the next frame.
        void MoveToNextFrame() {
            // Schedule a Signal command in the queue.
            ulong currentFenceValue = mFenceValues[mFrameIndex];
            mCommandQueue.Signal(mFence, currentFenceValue);

            // Update the frame index.
            mFrameIndex = mSwapChain.CurrentBackBufferIndex;
            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Signalled current fence value {currentFenceValue}, Frame index updated to {mFrameIndex} with fence value {nextFenceValue}",
                             nameof(MoveToNextFrame), currentFenceValue, mFrameIndex, mFenceValues[mFrameIndex]);

            // If the next frame is not ready to be rendered yet, wait until it is ready.
            if (mFence.CompletedValue < mFenceValues[mFrameIndex]) {
                mFenceEvent.Reset();
                mFence.SetEventOnCompletion(mFenceValues[mFrameIndex], mFenceEvent).CheckError();
                Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Waiting for {requiredFenceValue}, currently at {currentFenceValue}",
                                 nameof(MoveToNextFrame), mFenceValues[mFrameIndex], mFence.CompletedValue);
                mFenceEvent.WaitOne();
            }

            // Set the fence value for the next frame.
            mFenceValues[mFrameIndex] = currentFenceValue + 1;
            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Required fence value {requiredFenceValue} for Frame index {mFrameIndex}.",
                             nameof(MoveToNextFrame), mFenceValues[mFrameIndex], mFrameIndex);
        }

        protected static ReadOnlyMemory<byte> CompileBytecode(DxcShaderStage stage, string shaderName,
                                                              string entryPoint, DxcShaderModel? shaderModel = default) {
            string assetsPath = AppContext.BaseDirectory; // System.IO.Path.Combine(AppContext.BaseDirectory, "Shaders");
            string fileName = System.IO.Path.Combine(assetsPath, shaderName);
            string shaderSource = System.IO.File.ReadAllText(fileName);

            var options = new DxcCompilerOptions();
            if (shaderModel != null) {
                options.ShaderModel = shaderModel.Value;
            }
            else {
                options.ShaderModel = DxcShaderModel.Model6_4;
            }

            using (var includeHandler = new ShaderIncludeHandler(assetsPath)) {
                using IDxcResult results = DxcCompiler.Compile(stage, shaderSource, entryPoint, options,
                    fileName: fileName,
                    includeHandler: includeHandler);
                if (results.GetStatus().Failure) {
                    throw new Exception(results.GetErrors());
                }

                return results.GetObjectBytecodeMemory();
            }
        }

        private class ShaderIncludeHandler : CallbackBase, IDxcIncludeHandler {
            private readonly string[] _includeDirectories;
            private readonly Dictionary<string, SourceCodeBlob> _sourceFiles = new Dictionary<string, SourceCodeBlob>();

            public ShaderIncludeHandler(params string[] includeDirectories) {
                _includeDirectories = includeDirectories;
            }

            protected override void DisposeCore(bool disposing) {
                foreach (var pinnedObject in _sourceFiles.Values)
                    pinnedObject?.Dispose();

                _sourceFiles.Clear();
            }

            public Result LoadSource(string fileName, out IDxcBlob? includeSource) {
                if (fileName.StartsWith("./"))
                    fileName = fileName.Substring(2);

                var includeFile = GetFilePath(fileName);

                if (string.IsNullOrEmpty(includeFile)) {
                    includeSource = default;

                    return Result.Fail;
                }

                if (!_sourceFiles.TryGetValue(includeFile, out SourceCodeBlob? sourceCodeBlob)) {
                    byte[] data = NewMethod(includeFile);

                    sourceCodeBlob = new SourceCodeBlob(data);
                    _sourceFiles.Add(includeFile, sourceCodeBlob);
                }

                includeSource = sourceCodeBlob.Blob;

                return Result.Ok;
            }

            private static byte[] NewMethod(string includeFile) => System.IO.File.ReadAllBytes(includeFile);

            private string? GetFilePath(string fileName) {
                for (int i = 0; i < _includeDirectories.Length; i++) {
                    var filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(_includeDirectories[i], fileName));

                    if (System.IO.File.Exists(filePath))
                        return filePath;
                }

                return null;
            }


            private class SourceCodeBlob : IDisposable {
                private byte[] _data;
                private GCHandle _dataPointer;
                private IDxcBlobEncoding? _blob;

                internal IDxcBlob? Blob { get => _blob; }

                public SourceCodeBlob(byte[] data) {
                    _data = data;

                    _dataPointer = GCHandle.Alloc(data, GCHandleType.Pinned);

                    _blob = DxcCompiler.Utils.CreateBlob(_dataPointer.AddrOfPinnedObject(), data.Length, Vortice.Dxc.Dxc.DXC_CP_UTF8);
                }

                public void Dispose() {
                    //_blob?.Dispose();
                    _blob = null;

                    if (_dataPointer.IsAllocated)
                        _dataPointer.Free();
                    _dataPointer = default;
                }
            }
        }
    }
}
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
using wired.Graphics;
using wired.Rendering;

namespace D3D12HelloWorld.Mutiny {
    public partial class D3D12Mutiny : Form {
        const int FrameCount = 2;

        //Viewport dimensions
        float mAspectRatio;

        //Adapter info
        bool mUseWarpDevice;

        // Pipeline objects.
        Viewport mViewport;
        RawRect mScissorRect;
        IDXGISwapChain3 mSwapChain;
        ID3D12Device mDevice;
        readonly ID3D12CommandAllocator[] mCommandAllocators; //Potentially replaceable if migrating to the higher CommandList abstraction which creates its own allocator
        ID3D12RootSignature mRootSignature;  //Potentially replaceable by MaterialPass.PipelineState
        DescriptorAllocator mRtvHeap;
        DescriptorAllocator mSrvHeap;
        ID3D12PipelineState mPipelineState;  //Potentially replaceable by mGraphicsDevice
        ID3D12GraphicsCommandList mCommandList;  //Potentially replaceable by mGraphicsDevice.CommandList, but means we can't reuse mCommandAllocators
        GraphicsDevice mGraphicsDevice;

        // App resources.
        GraphicsResource mDirectionalLightGroupBuffer;
        GraphicsResource mGlobalBuffer;
        GraphicsResource mViewProjectionTransformBuffer;
        Sampler mDefaultSampler;
        ID3D12Resource mIndexBuffer;
        ID3D12Resource mVertexBuffer;
        IEnumerable<ShaderResourceView> mShaderResourceViews;
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

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public D3D12Mutiny(uint width, uint height, string name) {
            InitializeComponent();

            Width = Convert.ToInt32(width);
            Height = Convert.ToInt32(height);
            if (!string.IsNullOrEmpty(name))
                Text = name;

            mAspectRatio = width / (float)height;

            mViewport = new Viewport(width, height);
            mScissorRect = new RawRect(0, 0, Width, Height);
            mCommandAllocators = new ID3D12CommandAllocator[FrameCount];
            mFenceValues = new ulong[FrameCount];

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

            //mDirectionalLightGroupBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, sizeof(int) + System.Runtime.CompilerServices.Unsafe.SizeOf<DirectionalLightData>() * MaxLights, ResourceFlags.None, HeapType.Upload);
            //mGlobalBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, System.Runtime.CompilerServices.Unsafe.SizeOf<GlobalBuffer>(), ResourceFlags.None, HeapType.Upload);
            //mViewProjectionTransformBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, System.Runtime.CompilerServices.Unsafe.SizeOf<StereoViewProjectionTransform>(), ResourceFlags.None, HeapType.Upload);
            mDirectionalLightGroupBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            mGlobalBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            mViewProjectionTransformBuffer = GraphicsResource.CreateBuffer(mGraphicsDevice, 256, ResourceFlags.None, HeapType.Upload);
            mDefaultSampler = new Sampler(mDevice, mGraphicsDevice.SamplerAllocator);
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
            //Problem with using the higher level abstraction is that it does more than just execute the command list,
            //it also schedules a Signal command in the queue and increments the fence value for the next frame, and then waits for the GPU to complete.
            //The DirectX12GameEngine would next present the frame to the screen...
            //This differs from HelloTexture in that the command list would be executed (writing to the back buffer) and the previous frame would be presented
            //and then it would similarly schedule a Signal command in the queue, update the frame index to the current back buffer,
            //wait for the next frame to be rendered, and then increment the fence value for the next frame.
            //mGraphicsDevice.CommandList.Flush();
            mGraphicsDevice.DirectCommandQueue.ExecuteCommandList(mCommandList);
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
                mGraphicsDevice.CommandList.RenderTargets[i].Resource.Dispose();
            }
            mCommandList.Dispose();
            mGraphicsDevice.Dispose();

            mFenceEvent.Dispose();

            mRtvHeap.Dispose();
            mSwapChain.Dispose();

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
                    dxgiInfoQueue!.SetBreakOnSeverity(DXGI.DebugAll, Vortice.DXGI.Debug.InfoQueueMessageSeverity.Error, false);
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
                    d3dInfoQueue!.SetBreakOnSeverity(Vortice.Direct3D12.Debug.MessageSeverity.Error, false);
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

            //Rather than just a command queue, use the GraphicsDevice abstraction which creates CommandQueues and the CommandList, and an associated CommandAllocator
            mGraphicsDevice = new GraphicsDevice(mDevice);

            // Describe and create the swap chain.
            var backBufferFormat = Format.R8G8B8A8_UNorm;
            var swapChainDesc = new SwapChainDescription1 {
                BufferCount = FrameCount,
                Width = Width,
                Height = Height,
                Format = backBufferFormat,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = SampleDescription.Default,
                //AlphaMode = AlphaMode.Ignore,
            };

            // Swap chain needs the queue so that it can force a flush on it.
            using IDXGISwapChain1 swapChain = factory!.CreateSwapChainForHwnd(mGraphicsDevice.DirectCommandQueue.NativeCommandQueue, base.Handle, swapChainDesc);
            //factory.MakeWindowAssociation(base.Handle, WindowAssociationFlags.IgnoreAltEnter);
            mSwapChain = swapChain.QueryInterface<IDXGISwapChain3>();
            mFrameIndex = mSwapChain.CurrentBackBufferIndex;

            // Create descriptor heaps.
            {
                //if (mGraphicsDevice.RenderTargetViewAllocator.DescriptorHeap.Description.DescriptorCount != FrameCount) {
                //    mGraphicsDevice.RenderTargetViewAllocator.Dispose();
                //    mGraphicsDevice.RenderTargetViewAllocator = new DescriptorAllocator(mDevice, DescriptorHeapType.RenderTargetView, FrameCount);
                //}

                mRtvHeap = new DescriptorAllocator(mDevice, DescriptorHeapType.RenderTargetView, FrameCount);

                mSrvHeap = new DescriptorAllocator(mDevice, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1, DescriptorHeapFlags.ShaderVisible);
            }

            // Create frame resources.
            {
                // Create a RTV and a command allocator for each frame.
                var renderTargets = new RenderTargetView[FrameCount];
                for (int n = 0; n < swapChainDesc.BufferCount; n++) {
                    var renderTargetTexture = new Texture(mGraphicsDevice, mSwapChain.GetBuffer<ID3D12Resource>(n));
                    renderTargets[n] = RenderTargetView.FromTexture2D(renderTargetTexture, mRtvHeap);


                    mCommandAllocators[n] = mDevice.CreateCommandAllocator(CommandListType.Direct);
                    mCommandAllocators[n].Name = $"Direct Allocator {n}";
                }

                mGraphicsDevice.CommandList.SetRenderTargets(null, renderTargets);  //Ultimately calls OMSetRenderTargets after updating CommandList.RenderTargets accordingly
            }
        }

        /// <summary>
        /// Load the sample assets
        /// </summary>
        void LoadAssets() {
            // Create the root signature.
            {
                var highestVersion = mDevice.CheckHighestRootSignatureVersion(RootSignatureVersion.Version11);
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
                        var range1 = new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1, DescriptorRangeFlags.DataStatic); //I guess the -1 offset corresponds to D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND but have yet to successfully confirm it
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription1 {
                            Parameters = new[] { new RootParameter1(new RootDescriptorTable1(range1), ShaderVisibility.Pixel) },
                            StaticSamplers = new[] { sampler },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;

                    case RootSignatureVersion.Version10:
                    default:
                        var range = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1); //I guess the -1 offset corresponds to D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND but have yet to successfully confirm it
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription {
                            Parameters = new[] { new RootParameter(new RootDescriptorTable(range), ShaderVisibility.Pixel) },
                            StaticSamplers = new[] { sampler },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;
                }
                mRootSignature = mDevice.CreateRootSignature(0, rootSignatureDesc);
            }

            // Create the pipeline state, which includes compiling and loading shaders.
            {
                // Compile .NET to HLSL
                var shader = new HelloTexture.Shaders();
                var shaderGenerator = new DirectX12GameEngine.Shaders.ShaderGenerator(shader);
                DirectX12GameEngine.Shaders.ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                ReadOnlyMemory<byte> vertexShader = DirectX12GameEngine.Shaders.ShaderCompiler.Compile(DirectX12GameEngine.Shaders.ShaderStage.VertexShader, result.ShaderSource, nameof(shader.VSMain));
                ReadOnlyMemory<byte> pixelShader = DirectX12GameEngine.Shaders.ShaderCompiler.Compile(DirectX12GameEngine.Shaders.ShaderStage.PixelShader, result.ShaderSource, nameof(shader.PSMain));

                // Describe and create the graphics pipeline state object (PSO).
                var psoDesc = new GraphicsPipelineStateDescription {
                    InputLayout = new InputLayoutDescription(FlatShadedVertex.InputElements),
                    RootSignature = mRootSignature,
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                    RasterizerState = RasterizerDescription.CullNone, //I think this corresponds to CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT)
                    //BlendState = BlendDescription.Opaque,  //Nothing seems to correspond to CD3DX12_BLEND_DESC(D3D12_DEFAULT)
                    BlendState = BlendDescription.NonPremultiplied, //i.e. new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha),
                    DepthStencilState = DepthStencilDescription.None,  //Default value is DepthStencilDescription.Default
                    SampleMask = uint.MaxValue, //This is the default value anyway...
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                    RenderTargetFormats = new[] { Format.R8G8B8A8_UNorm, },
                    SampleDescription = SampleDescription.Default,
                };
                mPipelineState = mDevice.CreateGraphicsPipelineState(psoDesc);
            }

            // Create the command list.
            mCommandList = mDevice.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, mCommandAllocators[mFrameIndex], mPipelineState);
            //mCommandList = mDevice.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, mCommandAllocators[mFrameIndex], null);
            mCommandList.Name = "Direct Command List";

            // Command lists are created in the recording state, but there is nothing
            // to record yet. The main loop expects it to be closed, so close it now.
            mCommandList.Close();

            // Load the ship buffers (this one uses copy command queue, so need to create a fence for it first, so we can wait for it to finish)
            {
                var modelLoader = XModelLoader.Create3(mGraphicsDevice, @"..\..\..\Mutiny\Models\cannon_boss.X");
                (ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, IEnumerable<ShaderResourceView> ShaderResourceViews, Model Model) firstMesh
                    = System.Threading.Tasks.Task.Run(() => modelLoader.GetFlatShadedMeshesAsync(@"..\..\..\Mutiny", false)).Result.First();
                mIndexBuffer = firstMesh.IndexBuffer;
                mVertexBuffer = firstMesh.VertexBuffer;
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
            //mCommandList.Reset(commandAllocator, mPipelineState);
            mCommandList.Reset(commandAllocator, null);

            // Set necessary state.
            //mCommandList.SetGraphicsRootSignature(mRootSignature);  //Not set by CommandList, but by RenderSystem when looping through the meshes, after setting index and vertext buggers, before the primitive topology
            //mCommandList.SetDescriptorHeaps(1, new[] { mSrvHeap.DescriptorHeap });
            //If shader visible to start with...
            //D3D12 ERROR: CGraphicsCommandList::SetGraphicsRootDescriptorTable: Specified GPU Descriptor Handle (ptr = 0x215685a1090 at 0 offsetInDescriptorsFromDescriptorHeapStart), for Root Signature (0x000002156D6874F0:'Unnamed ID3D12RootSignature Object')'s Descriptor Table (at Parameter Index [0])'s Descriptor Range (at Range Index [0] of type D3D12_DESCRIPTOR_RANGE_TYPE_SRV) has not been initialized. All descriptors of descriptor ranges declared STATIC (not-DESCRIPTORS_VOLATILE) in a root signature must be initialized prior to being set on the command list. [ EXECUTION ERROR #646: INVALID_DESCRIPTOR_HANDLE]
            //mCommandList.SetGraphicsRootDescriptorTable(0, mSrvHeap.DescriptorHeap.GetGPUDescriptorHandleForHeapStart());

            //If not shader visible, need to copy to shader visible... where mSrvHeap.DescriptorHeap.GetCPUDescriptorHandleForHeapStart() would
            //represent the descriptor created on a non-shader visible heap, and mSrvHeap!.Allocate(1) and mSrvHeap.GetGpuDescriptorHandle(destinationDescriptor)
            //would be the shader-visible allocator...  If they are the same as right one, once can expect an error:
            //D3D12 ERROR: ID3D12Device::CopyDescriptorsSimple: Source ranges and dest ranges overlap, which results in undefined behavior. [ EXECUTION ERROR #653: COPY_DESCRIPTORS_INVALID_RANGES]
            //CpuDescriptorHandle destinationDescriptor = mSrvHeap!.Allocate(1);
            //mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mModel.Materials.First().ShaderResourceViewDescriptorSet!.StartCpuDescriptorHandle, mSrvHeap.DescriptorHeap.Description.Type);
            //GpuDescriptorHandle gpuDescriptor = mSrvHeap.GetGpuDescriptorHandle(destinationDescriptor);
            //mCommandList.SetGraphicsRootDescriptorTable(0, gpuDescriptor);


            mCommandList.SetDescriptorHeaps(descriptorHeaps.Length, descriptorHeaps.Select(d => d.DescriptorHeap).ToArray());  //Performed by CommandList.Reset
            //mCommandList.SetGraphicsRootDescriptorTable(0, mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator.DescriptorHeap.GetGPUDescriptorHandleForHeapStart());
            //mCommandList.SetGraphicsRootDescriptorTable(1, mGraphicsDevice.ShaderVisibleSamplerAllocator.DescriptorHeap.GetGPUDescriptorHandleForHeapStart());
            mCommandList.RSSetViewports(mViewport);  //Set during CommandList.ClearState
            mCommandList.RSSetScissorRects(mScissorRect);  //Set during CommandList.ClearState

            // Indicate that the back buffer will be used as a render target.
            var backBufferRenderTarget = mGraphicsDevice.CommandList.RenderTargets[mFrameIndex];
            mCommandList.ResourceBarrierTransition(backBufferRenderTarget.Resource.NativeResource, ResourceStates.Present, ResourceStates.RenderTarget);

            CpuDescriptorHandle rtvHandle = mRtvHeap.AllocateSlot(mFrameIndex);
            //mGraphicsDevice.CommandList.SetRenderTargets(null, rtvHandle);  //Ultimately calls OMSetRenderTargets after updating CommandList.RenderTargets accordingly
            mCommandList.OMSetRenderTargets(rtvHandle, null);  //Set during CommandList.ClearState

            // Record commands.
            var clearColor = new Vortice.Mathematics.Color(0, Convert.ToInt32(0.2f * 255.0f), Convert.ToInt32(0.4f * 255.0f), 255);
            mCommandList.ClearRenderTargetView(rtvHandle, clearColor);  //MyGame.BeginDraw
            //mCommandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

            var meshCount = mModel.Meshes.Count;
            var worldMatrixBuffers = new GraphicsResource[meshCount];
            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                worldMatrixBuffers[meshIndex] = GraphicsResource.CreateBuffer(mGraphicsDevice, 1 * Unsafe.SizeOf<Matrix4x4>(), ResourceFlags.None, HeapType.Upload);

                //If using bundles, this would be done after recording the commands
                worldMatrixBuffers[meshIndex].SetData(mModel.Meshes[meshIndex].WorldMatrix, 0);
            }

            RecordCommandList(mModel, mCommandList, srvDescriptorHeap, samplerDescriptorHeap, worldMatrixBuffers, 1);
            //var bundleCommandList = new CommandList(mGraphicsDevice, CommandListType.Bundle);
            //RecordCommandList(mModel, bundleCommandList, worldMatrixBuffers, 1);

            // Indicate that the back buffer will now be used to present.
            mCommandList.ResourceBarrierTransition(backBufferRenderTarget.Resource.NativeResource, ResourceStates.RenderTarget, ResourceStates.Present);

            mCommandList.Close();
        }

        private void RecordCommandList(Model model, ID3D12GraphicsCommandList commandList, DescriptorAllocator? srvDescriptorHeap, DescriptorAllocator? samplerDescriptorHeap, GraphicsResource[] worldMatrixBuffers, int instanceCount)
        {
            int renderTargetCount = 1; //We don't do stereo so this is always 1
            instanceCount *= renderTargetCount;

            for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[mesh.MaterialIndex];

                commandList.IASetIndexBuffer(mesh.MeshDraw.IndexBufferView);
                commandList.IASetVertexBuffers(0, mesh.MeshDraw.VertexBufferViews!);

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
                /*commandList.SetGraphicsRoot32BitConstant(rootParameterIndex++, renderTargetCount, 0);*/

                /*//commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mGlobalBuffer.DefaultConstantBufferView);
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
                */

                //commandList.SetGraphicsRootSampler(rootParameterIndex++, mDefaultSampler);
                //long gpuDescriptor = CopyDescriptors(samplerDescriptorHeap, mDefaultSampler.CpuDescriptorHandle, 1);
                /*CpuDescriptorHandle destinationDescriptor = samplerDescriptorHeap!.Allocate(1);
                mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(1, destinationDescriptor, mDefaultSampler.CpuDescriptorHandle, samplerDescriptorHeap.DescriptorHeap.Description.Type);
                GpuDescriptorHandle gpuDescriptor = samplerDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);*/

                if (material.ShaderResourceViewDescriptorSet != null) {
                    //commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, material.ShaderResourceViewDescriptorSet);
                    //SetGraphicsRootDescriptorTable(rootParameterIndex, srvDescriptorHeap, material.ShaderResourceViewDescriptorSet.StartCpuDescriptorHandle, material.ShaderResourceViewDescriptorSet.DescriptorCapacity);
                    //GpuDescriptorHandle value = CopyDescriptors(srvDescriptorHeap, material.ShaderResourceViewDescriptorSet.StartCpuDescriptorHandle, material.ShaderResourceViewDescriptorSet.DescriptorCapacity);
                    CpuDescriptorHandle destinationDescriptor = srvDescriptorHeap!.Allocate(material.ShaderResourceViewDescriptorSet.DescriptorCapacity);
                    mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(material.ShaderResourceViewDescriptorSet.DescriptorCapacity, destinationDescriptor, material.ShaderResourceViewDescriptorSet.StartCpuDescriptorHandle, srvDescriptorHeap.DescriptorHeap.Description.Type);
                    GpuDescriptorHandle gpuDescriptor = srvDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                    commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, gpuDescriptor);
                }

                /*if (material.SamplerDescriptorSet != null) {
                    //commandList.SetGraphicsRootDescriptorTable(rootParameterIndex++, material.SamplerDescriptorSet);
                    //SetGraphicsRootDescriptorTable(rootParameterIndex, samplerDescriptorHeap, material.SamplerDescriptorSet.StartCpuDescriptorHandle, material.SamplerDescriptorSet.DescriptorCapacity);
                    //GpuDescriptorHandle value = CopyDescriptors(samplerDescriptorHeap, material.SamplerDescriptorSet.StartCpuDescriptorHandle, material.SamplerDescriptorSet.DescriptorCapacity);
                    destinationDescriptor = samplerDescriptorHeap.Allocate(material.SamplerDescriptorSet.DescriptorCapacity);
                    mGraphicsDevice.NativeDevice.CopyDescriptorsSimple(material.SamplerDescriptorSet.DescriptorCapacity, destinationDescriptor, material.SamplerDescriptorSet.StartCpuDescriptorHandle, samplerDescriptorHeap.DescriptorHeap.Description.Type);
                    gpuDescriptor = samplerDescriptorHeap.GetGpuDescriptorHandle(destinationDescriptor);
                    commandList.SetGraphicsRootDescriptorTable(rootParameterIndex, gpuDescriptor);
                }*/

                if (mesh.MeshDraw.IndexBufferView != null) {
                    commandList.DrawIndexedInstanced(mesh.MeshDraw.IndexBufferView.Value.SizeInBytes / (mesh.MeshDraw.IndexBufferView.Value.Format.GetBitsPerPixel() >> 3), instanceCount, 0, 0, 0);
                }
                else {
                    var firstVertexBufferView = mesh.MeshDraw.VertexBufferViews!.First();
                    commandList.DrawInstanced(firstVertexBufferView.SizeInBytes / firstVertexBufferView.StrideInBytes, instanceCount, 0, 0);
                }
            }
        }

        private void RecordCommandList(Model model, CommandList commandList, GraphicsResource[] worldMatrixBuffers, int instanceCount)
        {
            int renderTargetCount = 1; //We don't do stereo so this is always 1
            instanceCount *= renderTargetCount;

            for (int meshIndex = 0; meshIndex < model.Meshes.Count(); meshIndex++)
            {
                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[mesh.MaterialIndex];

                //commandList.SetIndexBuffer(mesh.MeshDraw.IndexBufferView);
                commandList.SetVertexBuffers(0, mesh.MeshDraw.VertexBufferViews!);

                commandList.SetPipelineState(material.PipelineState!);
                commandList.SetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

                int rootParameterIndex = 0;
                commandList.SetGraphicsRoot32BitConstant(rootParameterIndex++, renderTargetCount, 0);

                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mGlobalBuffer.DefaultConstantBufferView);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mViewProjectionTransformBuffer.DefaultConstantBufferView);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, worldMatrixBuffers[meshIndex].DefaultConstantBufferView);
                //commandList.SetGraphicsRootConstantBufferView(rootParameterIndex++, mDirectionalLightGroupBuffer.DefaultConstantBufferView);
                commandList.SetGraphicsRootSampler(rootParameterIndex++, mDefaultSampler);

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

        // Wait for pending GPU work to complete.
        void WaitForGpu() {
            ulong requiredFenceValue = mFenceValues[mFrameIndex];
            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName} Signal current fence value {currentFenceValue} for frame {mFrameIndex}",
                             nameof(WaitForGpu), requiredFenceValue, mFrameIndex);

            // Schedule a Signal command in the queue.
            //TODO: Work out how to change this to remove dependency on internal member: NativeCommandQueue
            mGraphicsDevice.DirectCommandQueue.NativeCommandQueue.Signal(mFence, requiredFenceValue);

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
            //TODO: Work out how to change this to remove dependency on internal member: NativeCommandQueue
            mGraphicsDevice.DirectCommandQueue.NativeCommandQueue.Signal(mFence, currentFenceValue);

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
    }
}
using DirectX12GameEngine.Shaders;
using Serilog;
using SharpGen.Runtime;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;
using wired;
using wired.Assets;
using wired.Graphics;
using wired.Rendering;

namespace D3D12Bundles {
    /// <summary>
    /// https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/Desktop/D3D12Bundles/src/D3D12Bundles.cpp
    /// </summary>
    public partial class D3D12Bundles : Form {
        const int FrameCount = 3;
        const int CityRowCount = 10;
        const int CityColumnCount = 3;
        const bool UseBundles = true;

        struct Vertex {
            /// <summary>
            /// Defines the vertex input layout.
            /// NOTE: The HLSL Semantic names here must match the ShaderTypeAttribute.TypeName associated with the ShaderSemanticAttribute associated with the 
            ///       compiled Vertex Shader's Input parameters - PositionSemanticAttribute and TextureCoordinateSemantic in this case per the VSInput struct
            /// </summary>
            public static readonly InputElementDescription[] InputElements = new[] {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0, InputClassification.PerVertexData, 0),
                new InputElementDescription("TANGENT", 0, Format.R32G32B32_Float, 32, 0, InputClassification.PerVertexData, 0),
            };

            public Vertex(in Vector3 position, in Vector3 normal, in Vector2 texCoord, in Vector3 tangent) {
                Position = position;
                Normal = normal;
                TexCoord = texCoord;
                Tangent = tangent;
            }

            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector2 TexCoord;
            public readonly Vector3 Tangent;
        };

        readonly string mName;
        readonly ILogger mLogger;

        //DXSample - Viewport dimensions
        float mAspectRatio;

        //DXSample - Adapter info
        bool mUseWarpDevice = false;

        // Pipeline objects.
        Viewport mViewport;
        RawRect mScissorRect;
        GraphicsPresenter mPresenter;
        GraphicsDevice mGraphicsDevice;
        ID3D12RootSignature mRootSignature;
        PipelineState mPipelineState1;
        PipelineState mPipelineState2;

        // App resources.
        int mNumIndices;
        ID3D12Resource mVertexBuffer;
        ID3D12Resource mIndexBuffer;
        VertexBufferView mVertexBufferView;
        IndexBufferView? mIndexBufferView;
        Texture mTexture;
        Sampler mDefaultSampler;
        StepTimer mTimer;
        SimpleCamera mCamera;

        // Frame resources.
        List<FrameResource> mFrameResources;
        FrameResource mCurrentFrameResource;
        int mCurrentFrameResourceIndex;

        // Synchronization objects.
        int mFrameCounter;

        //DX12GE - GameBase
        private readonly object mTickLock = new object();

        public D3D12Bundles() : this(1200, 900, string.Empty, Log.Logger) {
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public D3D12Bundles(uint width, uint height, string name, ILogger logger) {
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
            mCamera = new SimpleCamera();
            mFrameResources = new List<FrameResource>();

            OnInit();

            CompositionTarget.Rendering += HandleCompositionTarget_Rendering;
            this.FormClosing += (object? sender, FormClosingEventArgs e) => OnDestroy();
            this.KeyDown += (object? sender, KeyEventArgs e) => mCamera.OnKeyDown(e.KeyData);
            this.KeyUp += (object? sender, KeyEventArgs e) => mCamera.OnKeyUp(e.KeyData);
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private void HandleCompositionTarget_Rendering(object? sender, EventArgs e) {
            lock (mTickLock) {
                OnUpdate();

                OnRender();
            }
        }

        public virtual void OnInit() {
            mCamera.Init(new Vector3(8, 8, 30));

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
                var fps = $"{mTimer.FramesPerSecond}fps";
                //SetCustomWindowText(fps);
                //Which ultimately calls: SetWindowText($"{mName}: {fps}");
                Text = $"{mName}: {fps}";
                mFrameCounter = 0;
            }

            mFrameCounter++;

            // Move to the next frame resource.
            mCurrentFrameResourceIndex = (mCurrentFrameResourceIndex + 1) % FrameCount;
            mCurrentFrameResource = mFrameResources[mCurrentFrameResourceIndex];

            // Make sure that this frame resource isn't still in use by the GPU.
            // If it is, wait for it to complete, because resources still scheduled for GPU execution
            // cannot be modified or else undefined behavior will result.
            if (mCurrentFrameResource.mFenceValue != 0) {
                mGraphicsDevice.DirectCommandQueue.WaitForSignal(mCurrentFrameResource.mFenceValue);
            }

            mCamera.Update((float)mTimer.ElapsedSeconds);
            mCurrentFrameResource.UpdateConstantBuffers(mCamera.GetViewMatrix(), mCamera.GetProjectionMatrix(0.8f, mAspectRatio));
        }

        /// <summary>
        /// Render the scene
        /// </summary>
        public virtual void OnRender() {
            using (var pe = new ProfilingEvent(mGraphicsDevice.DirectCommandQueue.NativeCommandQueue, "Render", mLogger)) {
                // Record all the commands we need to render the scene into the command list.
                CompiledCommandList compiledCommandList = PopulateCommandList(mGraphicsDevice.CommandList, mCurrentFrameResource);

                // Execute the command list.
                mGraphicsDevice.DirectCommandQueue.ExecuteCommandList(compiledCommandList);
            }

            // Present and update the frame index for the next frame.
            mPresenter.Present();

            // Signal and increment the fence value.
            mCurrentFrameResource.mFenceValue = mGraphicsDevice.DirectCommandQueue.AddSignal();
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

            foreach (var frameResource in mFrameResources) {
                frameResource.Dispose();
            }

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
            // NOTE: Ensure native debugging is enabled to see the debug messages from this native code in the Visual Studio Debug window!
            {
                if (D3D12.D3D12GetDebugInterface(out ID3D12Debug? debugController).Success) {
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
                using ID3D12InfoQueue? d3dInfoQueue = device.QueryInterfaceOrNull<ID3D12InfoQueue>();
                if (d3dInfoQueue != null) {
                    d3dInfoQueue.SetBreakOnSeverity(MessageSeverity.Corruption, true);
                    d3dInfoQueue.SetBreakOnSeverity(MessageSeverity.Error, true);
                    var hide = new MessageId[] {
                        MessageId.MapInvalidNullRange,
                        MessageId.UnmapInvalidNullRange,
                        // Workarounds for debug layer issues on hybrid-graphics systems
                        MessageId.ExecuteCommandListsWrongSwapChainBufferReference,
                        MessageId.ResourceBarrierMismatchingCommandListType,
                    };

                    var filter = new InfoQueueFilter {
                        DenyList = new InfoQueueFilterDescription {
                            Ids = hide
                        }
                    };
                    d3dInfoQueue.AddStorageFilterEntries(filter);

                    d3dInfoQueue.AddMessage(MessageCategory.Miscellaneous, MessageSeverity.Warning, MessageId.SamplePositionsMismatchRecordTimeAssumedFromClear, $"Hi, from {mName}");
                    d3dInfoQueue.AddApplicationMessage(MessageSeverity.Warning, $"Hi, from application {mName}");
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
        }

        /// <summary>
        /// Load the sample assets
        /// </summary>
        void LoadAssets() {
            // Note: ComPtr's are CPU objects but these resources need to stay in scope until
            // the command list that references them has finished executing on the GPU.
            // We will flush the GPU at the end of this method to ensure the resources are not
            // prematurely destroyed.
            //ID3D12Resource vertexBufferUploadHeap;
            //ID3D12Resource indexBufferUploadHeap;
            ID3D12Resource textureUploadHeap;

            // Create the root signature.
            {
                // This is the highest version the sample supports. If CheckHighestRootSignatureVersion succeeds, the HighestVersion returned will not be greater than this.
                var highestVersion = mGraphicsDevice.NativeDevice.CheckHighestRootSignatureVersion(RootSignatureVersion.Version11);
                VersionedRootSignatureDescription rootSignatureDesc;
                switch (highestVersion) {
                    case RootSignatureVersion.Version11:
                        var range111 = new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1, DescriptorRangeFlags.DataStatic);
                        var range112 = new DescriptorRange1(DescriptorRangeType.Sampler, 1, 0);
                        var range113 = new DescriptorRange1(DescriptorRangeType.ConstantBufferView, 1, 0, 0, -1, DescriptorRangeFlags.DataStatic);
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription1 {
                            Parameters = new[] {
                                new RootParameter1(new RootDescriptorTable1(range111), ShaderVisibility.Pixel),
                                new RootParameter1(new RootDescriptorTable1(range112), ShaderVisibility.Pixel),
                                new RootParameter1(new RootDescriptorTable1(range113), ShaderVisibility.All),
                            },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;

                    case RootSignatureVersion.Version10:
                    default:
                        var range101 = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1);
                        var range102 = new DescriptorRange(DescriptorRangeType.Sampler, 1, 0);
                        var range103 = new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 0, 0, -1);
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription {
                            Parameters = new[] {
                                new RootParameter(new RootDescriptorTable(range101), ShaderVisibility.Pixel),
                                new RootParameter(new RootDescriptorTable(range102), ShaderVisibility.Pixel),
                                new RootParameter(new RootDescriptorTable(range103), ShaderVisibility.All),
                            },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;
                }
                mRootSignature = mGraphicsDevice.NativeDevice.CreateRootSignature(0, rootSignatureDesc);
                mRootSignature.Name = nameof(mRootSignature);
            }

            // Create the pipeline state, which includes loading shaders.
            {
                // Compile .NET to HLSL
                var shader1 = new SimpleShader();
                var shaderGenerator = new ShaderGenerator(shader1);
                ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                byte[] vertexShader = ShaderCompiler.Compile(ShaderStage.VertexShader, result.ShaderSource, nameof(shader1.VSMain));
                byte[] pixelShader1 = ShaderCompiler.Compile(ShaderStage.PixelShader, result.ShaderSource, nameof(shader1.PSMain));

                var shader2 = new AltShader();
                shaderGenerator = new ShaderGenerator(shader2);
                result = shaderGenerator.GenerateShader();
                byte[] pixelShader2 = ShaderCompiler.Compile(ShaderStage.PixelShader, result.ShaderSource, nameof(shader2.PSMain));

                // Describe and create the graphics pipeline state objects (PSO).
                mPipelineState1 = new PipelineState(mGraphicsDevice, mRootSignature, FlatShadedVertex.InputElements,
                                                    vertexShader, pixelShader1, name: nameof(mPipelineState1));

                // Duplicate the description but use an alternate pixel shader and create a second PSO.
                mPipelineState2 = new PipelineState(mGraphicsDevice, mRootSignature, FlatShadedVertex.InputElements,
                                                    vertexShader, pixelShader2, name: nameof(mPipelineState2));
            }

            // Reset the command list, we need it open for initial GPU setup.
            mGraphicsDevice.CommandList.Reset();

            // Read in mesh data for vertex/index buffers.
            {
                var modelLoader = XModelLoader.Create3(mGraphicsDevice, @"..\..\..\..\D3D12HelloWorld\Mutiny\Models\cannon_boss.X");
                (ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, IEnumerable<ShaderResourceView> ShaderResourceViews, Model Model) firstMesh
                    = Task.Run(() => modelLoader.GetFlatShadedMeshesAsync(@"..\..\..\..\D3D12HelloWorld\Mutiny", false)).Result.First();

                mVertexBuffer = firstMesh.VertexBuffer;
                mVertexBuffer.Name = nameof(mVertexBuffer);
                mVertexBufferView = firstMesh.Model.Meshes[0].MeshDraw.VertexBufferViews![0];

                mIndexBuffer = firstMesh.IndexBuffer;
                mIndexBufferView = firstMesh.Model.Meshes[0].MeshDraw.IndexBufferView;
                //mModel = firstMesh.Model;
                //mShaderResourceViews = firstMesh.ShaderResourceViews;
                mNumIndices = mIndexBufferView!.Value.SizeInBytes / (mIndexBufferView.Value.Format.GetBitsPerPixel() >> 3);
            }

            /*// Create the vertex buffer.
            {
                int vertexBufferSize = triangleVertices.Length * Unsafe.SizeOf<Vertex>();  //const UINT SampleAssets::VertexDataSize = 820248;
                mVertexBuffer = mGraphicsDevice.NativeDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None,
                                                                ResourceDescription.Buffer(vertexBufferSize), ResourceStates.CopyDest, null);

                vertexBufferUploadHeap = mGraphicsDevice.NativeDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                                         ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead, null);

                mVertexBuffer.Name = nameof(mVertexBuffer);

                // Copy data to the intermediate upload heap and then schedule a copy 
                // from the upload heap to the vertex buffer.
                //var vertexData = new SubresourceInfo {
                //    Offset = pMeshData + SampleAssets::VertexDataOffset,
                //    RowPitch = SampleAssets::VertexDataSize,
                //    DepthPitch = vertexData.RowPitch,
                //};
                mGraphicsDevice.NativeDevice.UpdateSubresource(mCommandList, mVertexBuffer, vertexBufferUploadHeap, 0, 0, vertexData);
                mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mVertexBuffer, ResourceStates.CopyDest, ResourceStates.VertexAndConstantBuffer));

                // Initialize the vertex buffer view.
                mVertexBufferView.BufferLocation = mVertexBuffer.GPUVirtualAddress;
                mVertexBufferView.StrideInBytes = Unsafe.SizeOf<Vertex>();
                mVertexBufferView.SizeInBytes = vertexBufferSize;
            }*/

            /*// Create the index buffer.
            {
                int indexBufferSize = meshIndices.Length * Unsafe.SizeOf<Vertex>();  //const UINT SampleAssets::IndexDataSize = 1344536;
                mIndexBuffer = mGraphicsDevice.NativeDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None,
                                                               ResourceDescription.Buffer(indexBufferSize), ResourceStates.CopyDest, null);

                indexBufferUploadHeap = mGraphicsDevice.NativeDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                                        ResourceDescription.Buffer(indexBufferSize), ResourceStates.GenericRead, null);

                mIndexBuffer.Name = nameof(mIndexBuffer);

                // Copy data to the intermediate upload heap and then schedule a copy 
                // from the upload heap to the index buffer.
                //var indexData = new SubresourceInfo {
                //    Offset = pMeshData + SampleAssets::IndexDataOffset,
                //    RowPitch = SampleAssets::IndexDataSize,
                //    DepthPitch = indexData.RowPitch,
                //};
                mGraphicsDevice.NativeDevice.UpdateSubresource(mCommandList, mIndexBuffer, indexBufferUploadHeap, 0, 0, indexData);
                mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mIndexBuffer, ResourceStates.CopyDest, ResourceStates.IndexBuffer));

                // Describe the index buffer view.
                mIndexBufferView.BufferLocation = mVertexBuffer.GPUVirtualAddress;
                mIndexBufferView.Format = SampleAssets::StandardIndexFormat;
                mIndexBufferView.SizeInBytes = indexBufferSize;

                mNumIndices = SampleAssets::IndexDataSize / 4;    // R32_UINT (SampleAssets::StandardIndexFormat) = 4 bytes each.
            }*/

            // Create the texture and sampler.
            {
                // Describe and create a Texture2D.
                ResourceDescription textureDesc;
                var textureFile = new FileInfo(@"..\..\..\..\D3D12HelloWorld\Mutiny\Textures\CannonBoss_tex.jpg");
                if (!textureFile.Exists) {
                    throw new FileNotFoundException($"Could not find file: {textureFile.FullName}");
                }
                else {
                    using FileStream stream = File.OpenRead(textureFile.FullName);
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var firstFrame = decoder.Frames.First();

                    var format = firstFrame.Format.BitsPerPixel == 32
                               ? Format.R8G8B8A8_UNorm
                               : Format.D24_UNorm_S8_UInt; //Used for a depth stencil, for a 24bit image consider Format.R8G8B8_UNorm
                    var pixelSizeInBytes = firstFrame.Format.BitsPerPixel / 8;
                    var stride = firstFrame.PixelWidth * pixelSizeInBytes;
                    var pixels = new byte[firstFrame.PixelHeight * stride];
                    firstFrame.CopyPixels(pixels, stride, 0);

                    ushort mipLevels = 1;
                    textureDesc = ResourceDescription.Texture2D(format, (uint)firstFrame.PixelWidth, (uint)firstFrame.PixelHeight, 1, mipLevels, 1, 0, ResourceFlags.None);
                    var textureResource = mGraphicsDevice.NativeDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None,
                                                                                               textureDesc, ResourceStates.CopyDest, null);
                    mTexture = new Texture(mGraphicsDevice, textureResource, nameof(mTexture));

                    var subresourceCount = textureDesc.DepthOrArraySize * textureDesc.MipLevels;
                    var uploadBufferSize = mGraphicsDevice.NativeDevice.GetRequiredIntermediateSize(mTexture.NativeResource, 0, subresourceCount);

                    textureUploadHeap = mGraphicsDevice.NativeDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                                                             ResourceDescription.Buffer(uploadBufferSize), ResourceStates.GenericRead, null);

                    Span<byte> textureData = pixels.AsSpan();

                    mGraphicsDevice.CommandList.UpdateSubresource(mTexture, textureUploadHeap, 0, 0, textureData);
                    /*var textureData = new SubresourceInfo[subresourceCount];
                    textureData[0] = new SubresourceInfo {
                        Offset = pMeshData + SampleAssets::Textures[0].Data[0].Offset,
                        RowPitch = SampleAssets::Textures[0].Data[0].Pitch,
                        DepthPitch = SampleAssets::Textures[0].Data[0].Size,
                    };
                    mGraphicsDevice.NativeDevice.UpdateSubResources(mCommandList, mTexture, textureUploadHeap, 0, 0, textureData);*/
                }

                //NOTE: This is not required if using a copy queue, see MJP comment at https://www.gamedev.net/forums/topic/704025-use-texture2darray-in-d3d12/
                mGraphicsDevice.CommandList.ResourceBarrierTransition(mTexture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

                // Describe and create a sampler.
                //var samplerDesc = new SamplerDescription {
                //    Filter = Filter.MinMagMipLinear,
                //    AddressU = TextureAddressMode.Wrap,
                //    AddressV = TextureAddressMode.Wrap,
                //    AddressW = TextureAddressMode.Wrap,
                //    MinLOD = 0.0f,
                //    MaxLOD = float.MaxValue,
                //    MipLODBias = 0.0f,
                //    MaxAnisotropy = 1,
                //    ComparisonFunction = ComparisonFunction.Always,
                //};
                var samplerDesc = new SamplerDescription {
                    Filter = Filter.MinMagMipPoint,
                    AddressU = TextureAddressMode.Border,
                    AddressV = TextureAddressMode.Border,
                    AddressW = TextureAddressMode.Border,
                    MipLODBias = 0,
                    MaxAnisotropy = 0,
                    ComparisonFunction = ComparisonFunction.Never,
                    BorderColor = new Color4(Color3.Black, 0.0f),
                    MinLOD = 0.0f,
                    MaxLOD = 0.0f,
                };
                mDefaultSampler = new Sampler(mGraphicsDevice.NativeDevice, mGraphicsDevice.ShaderVisibleSamplerAllocator, samplerDesc);
                
                // Describe and create a SRV for the texture.
                var srvDesc = new ShaderResourceViewDescription {
                    Shader4ComponentMapping = ShaderComponentMapping.DefaultComponentMapping(), //D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,  //i.e. default 1:1 mapping
                    Format = textureDesc.Format,
                    ViewDimension = ShaderResourceViewDimension.Texture2D,
                };
                srvDesc.Texture2D.MipLevels = 1;
                mGraphicsDevice.NativeDevice.CreateShaderResourceView(mTexture.NativeResource, srvDesc, mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator.Allocate(1));
            }

            // Close the command list and execute it to begin the initial GPU setup.
            // This higher level abstraction will also wait until the command list finishes execution,
            // which means the assets have been uploaded to the GPU before we continue.
            mGraphicsDevice.DirectCommandQueue.ExecuteCommandLists(mGraphicsDevice.CommandList.Close());

            CreateFrameResources();
        }

        /// <summary>
        /// Create the resources that will be used every frame.
        /// </summary>
        void CreateFrameResources() {
            // Initialize each frame resource.
            // Because using mCbvSrvHeap.Allocate now instead of mCbvSrvHeap.DescriptorHeap.GetCPUDescriptorHandleForHeapStart,
            // this will have already moved past the SRV in slot 1.
            for (var i = 0; i < FrameCount; i++) {
                var pFrameResource = new FrameResource(mGraphicsDevice, CityRowCount, CityColumnCount);

                ulong cbOffset = 0;
                for (var j = 0; j < CityRowCount; j++) {
                    for (var k = 0; k < CityColumnCount; k++) {
                        // Describe and create a constant buffer view (CBV).
                        var cbvDesc = new ConstantBufferViewDescription {
                            BufferLocation = pFrameResource.mCbvUploadHeap.GPUVirtualAddress + cbOffset,
                            SizeInBytes = Unsafe.SizeOf<FrameResource.SceneConstantBuffer>()
                        };
                        cbOffset += (ulong)cbvDesc.SizeInBytes;
                        mGraphicsDevice.NativeDevice.CreateConstantBufferView(cbvDesc, mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator.Allocate(1));
                    }
                }

                pFrameResource.InitBundle(mGraphicsDevice, mPipelineState1, mPipelineState2, i, mNumIndices, mIndexBufferView,
                                          mVertexBufferView, mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator, mGraphicsDevice.ShaderVisibleSamplerAllocator, mRootSignature);

                mFrameResources.Add(pFrameResource);
            }
        }

        CompiledCommandList PopulateCommandList(CommandList commandList, FrameResource frameResource) {
            // Command list allocators can only be reset when the associated
            // command lists have finished execution on the GPU; apps should use
            // fences to determine GPU execution progress.
            mCurrentFrameResource.mCommandAllocator.Reset();

            // However, when ExecuteCommandList() is called on a particular command
            // list, that command list can then be reset at any time and must be before
            // re-recording.
            commandList.Reset(mCurrentFrameResource.mCommandAllocator, mPipelineState1);

            // Set necessary state.
            commandList.SetNecessaryState(mRootSignature, mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator.DescriptorHeap, mGraphicsDevice.ShaderVisibleSamplerAllocator.DescriptorHeap);

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
            commandList.ClearDepthStencilView(commandList.DepthStencilBuffer, ClearFlags.Depth, 1.0f, 0);

            if (UseBundles) {
                // Execute the prebuilt bundle.
                commandList.ExecuteBundle(frameResource.mBundle!);
            }
            else {
                // Populate a new command list.
                frameResource.PopulateCommandList(commandList, mPipelineState1, mPipelineState2, mCurrentFrameResourceIndex,
                                                  mNumIndices, mIndexBufferView, mVertexBufferView,
                                                  mGraphicsDevice.ShaderVisibleShaderResourceViewAllocator, mGraphicsDevice.ShaderVisibleSamplerAllocator, mRootSignature);
            }

            // Indicate that the back buffer will now be used to present.
            commandList.ResourceBarrierTransition(mPresenter.BackBuffer.Resource, ResourceStates.RenderTarget, ResourceStates.Present);

            return commandList.Close();
        }

    }
}
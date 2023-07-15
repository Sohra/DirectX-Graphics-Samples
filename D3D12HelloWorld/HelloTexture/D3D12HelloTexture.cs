using D3D12HelloWorld.Rendering;
using DirectX12GameEngine.Shaders;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Media;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace D3D12HelloWorld.HelloTexture {
    /// <summary>
    /// https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/UWP/D3D12HelloWorld/src/HelloTexture/D3D12HelloTexture.cpp
    /// </summary>
    public partial class D3D12HelloTexture : Form
    {
        const int FrameCount = 2;
        const int TextureWidth = 256;
        const int TextureHeight = 256;
        const int TexturePixelSize = 4;  // The number of bytes used to represent a pixel in the texture.

        struct Vertex
        {
            public Vector3 Position;
            public Vector2 TexCoord;
        };

        //DXSample - Viewport dimensions
        float mAspectRatio;

        //DXSample - Adapter info
        bool mUseWarpDevice;

        // Pipeline objects.
        Viewport mViewport;
        RawRect mScissorRect;
        IDXGISwapChain3 mSwapChain;
        ID3D12Device mDevice;
        readonly ID3D12Resource[] mRenderTargets;
        ID3D12CommandAllocator mCommandAllocator;
        ID3D12CommandQueue mCommandQueue;
        ID3D12RootSignature mRootSignature;
        ID3D12DescriptorHeap mRtvHeap;
        ID3D12DescriptorHeap mSrvHeap;
        ID3D12PipelineState mPipelineState;
        ID3D12GraphicsCommandList mCommandList;
        int mRtvDescriptorSize;

        // App resources.
        ID3D12Resource mVertexBuffer;
        VertexBufferView mVertexBufferView;
        ID3D12Resource mIndexBuffer;
        IndexBufferView? mIndexBufferView;
        ID3D12Resource mTexture;

        // Synchronization objects.
        int mFrameIndex;
        ManualResetEvent mFenceEvent;
        ID3D12Fence mFence;
        ulong mFenceValue;

        //DX12GE - GameBase
        private readonly object mTickLock = new object();

        public D3D12HelloTexture() : this(1200, 900, string.Empty)
        {
        }

        public D3D12HelloTexture(uint width, uint height, string name)
        {
            InitializeComponent();

            Width = Convert.ToInt32(width);
            Height = Convert.ToInt32(height);
            if (!string.IsNullOrEmpty(name))
                Text = name;

            mAspectRatio = width / (float)height;

            mViewport = new Viewport(width, height);
            mScissorRect = new RawRect(0, 0, Width, Height);
            mRenderTargets = new ID3D12Resource[FrameCount];

            OnInit();

            CompositionTarget.Rendering += HandleCompositionTarget_Rendering;
            this.FormClosing += (object? sender, FormClosingEventArgs e) => OnDestroy();
        }

        private void HandleCompositionTarget_Rendering(object? sender, EventArgs e)
        {
            lock (mTickLock)
            {
                OnUpdate();

                OnRender();
            }
        }

        public virtual void OnInit()
        {
            LoadPipeline();
            LoadAssets();
        }

        /// <summary>
        /// Update frame-based values
        /// </summary>
        public virtual void OnUpdate()
        {
        }

        /// <summary>
        /// Render the scene
        /// </summary>
        public virtual void OnRender()
        {
            // Record all the commands we need to render the scene into the command list.
            PopulateCommandList();

            // Execute the command list.
            mCommandQueue.ExecuteCommandList(mCommandList);

            // Present the frame.
            var presentResult = mSwapChain.Present(1, 0);
            if (presentResult.Failure)
                throw new COMException("SwapChain.Present failed.", presentResult.Code);

            WaitForPreviousFrame();
        }

        public virtual void OnDestroy()
        {
            // Ensure that the GPU is no longer referencing resources that are about to be
            // cleaned up by the destructor.
            WaitForPreviousFrame();

            mFenceEvent.Dispose();
            //Replacing CloseHandle(mFenceEvent);
        }

        /// <summary>
        /// Load the rendering pipeline dependencies.
        /// </summary>
        void LoadPipeline()
        {
            bool dxgiFactoryDebugMode = false; //int dxgiFactoryFlags = 0;

#if DEBUG
            // Enable the debug layer (requires the Graphics Tools "optional feature").
            // NOTE: Enabling the debug layer after device creation will invalidate the active device.
            {
                Result debugResult = D3D12.D3D12GetDebugInterface(out ID3D12Debug? debugController);

                if (debugResult.Success)
                {
                    ID3D12Debug1 debug = debugController!.QueryInterface<ID3D12Debug1>();

                    debug.EnableDebugLayer();

                    // Enable additional debug layers.
                    dxgiFactoryDebugMode = true; //dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG; //0x01
                }

            }
#endif

            DXGI.CreateDXGIFactory2(dxgiFactoryDebugMode, out IDXGIFactory4? factory);

            if (mUseWarpDevice)
            {
                Result warpResult = factory!.EnumWarpAdapter(out IDXGIAdapter? warpAdapter);
                if (warpResult.Failure)
                    throw new COMException("EnumWarpAdaptor creation failed", warpResult.Code);

                mDevice = D3D12.D3D12CreateDevice<ID3D12Device>(warpAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }
            else
            {
                IDXGIAdapter1? hardwareAdapter = null;
                //We could pull this from https://github.com/microsoft/DirectX-Graphics-Samples/blob/3e8b39eba5facbaa3cd26d4196452987ac34499d/Samples/UWP/D3D12HelloWorld/src/HelloTriangle/DXSample.cpp#L43
                //But for now, leave it up to Vortice to figure out...
                //GetHardwareAdapter(factory.Get(), &hardwareAdapter);  

                mDevice = D3D12.D3D12CreateDevice<ID3D12Device>(hardwareAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }

            // Describe and create the command queue.
            mCommandQueue = mDevice.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));

            // Describe and create the swap chain.
            var swapChainDesc = new SwapChainDescription1
            {
                BufferCount = FrameCount,
                Width = Width,
                Height = Height,
                Format = Format.R8G8B8A8_UNorm,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = new SampleDescription(1, 0),
            };

            // Swap chain needs the queue so that it can force a flush on it.
            using IDXGISwapChain1 swapChain = factory!.CreateSwapChainForHwnd(mCommandQueue, base.Handle, swapChainDesc);
            mSwapChain = swapChain.QueryInterface<IDXGISwapChain3>();
            mFrameIndex = mSwapChain.CurrentBackBufferIndex;

            // Create descriptor heaps.
            {
                // Describe and create a render target view (RTV) descriptor heap.
                var rtvHeapDesc = new DescriptorHeapDescription
                {
                    DescriptorCount = FrameCount,
                    Type = DescriptorHeapType.RenderTargetView,
                    Flags = DescriptorHeapFlags.None,
                };
                mRtvHeap = mDevice.CreateDescriptorHeap(rtvHeapDesc);

                // Describe and create a shader resource view (SRV) heap for the texture.
                var srvHeapDesc = new DescriptorHeapDescription
                {
                    DescriptorCount = 1,
                    Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                    Flags = DescriptorHeapFlags.ShaderVisible,
                };
                mSrvHeap = mDevice.CreateDescriptorHeap(srvHeapDesc);

                mRtvDescriptorSize = mDevice.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);  //Or mRtvHeap.Description.Type
            }

            // Create frame resources.
            {
                CpuDescriptorHandle rtvHandle = mRtvHeap.GetCPUDescriptorHandleForHeapStart();

                // Create a RTV for each frame.
                for (int n = 0; n < FrameCount; n++)
                {
                    mRenderTargets[n] = mSwapChain.GetBuffer<ID3D12Resource>(n);

                    mDevice.CreateRenderTargetView(mRenderTargets[n], null, rtvHandle);
                    rtvHandle += 1 * mRtvDescriptorSize;
                }
            }

            mCommandAllocator = mDevice.CreateCommandAllocator(CommandListType.Direct);
        }

        /// <summary>
        /// Load the sample assets
        /// </summary>
        void LoadAssets()
        {
            // Create the root signature.
            {
                var highestVersion = mDevice.CheckHighestRootSignatureVersion(RootSignatureVersion.Version11);
                var sampler = new StaticSamplerDescription
                {
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
                switch (highestVersion)
                {
                    case RootSignatureVersion.Version11:
                        var range1 = new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1, DescriptorRangeFlags.DataStatic); //I guess the -1 offset corresponds to D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND but have yet to successfully confirm it
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription1
                        {
                            Parameters = new[] { new RootParameter1(new RootDescriptorTable1(range1), ShaderVisibility.Pixel) },
                            StaticSamplers = new[] { sampler },
                            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                        });
                        break;

                    case RootSignatureVersion.Version10:
                    default:
                        var range = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, -1); //I guess the -1 offset corresponds to D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND but have yet to successfully confirm it
                        rootSignatureDesc = new VersionedRootSignatureDescription(new RootSignatureDescription
                        {
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
                var shader = new Shaders();
                var shaderGenerator = new ShaderGenerator(shader);
                ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                ReadOnlyMemory<byte> vertexShader = ShaderCompiler.Compile(ShaderStage.VertexShader, result.ShaderSource, nameof(shader.VSMain));
                ReadOnlyMemory<byte> pixelShader = ShaderCompiler.Compile(ShaderStage.PixelShader, result.ShaderSource, nameof(shader.PSMain));

                // Define the vertex input layout.
                //NOTE: The HLSL Semantic names here must match the ShaderTypeAttribute.TypeName associated with the ShaderSemanticAttribute associated with the 
                //      compiled Vertex Shader's Input parameters - PositionSemanticAttribute and TextureCoordinateSemantic in this case per the VSInput struct
                var inputElementDescs = new[]
                {
                    new InputElementDescription("Position", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                    new InputElementDescription("TexCoord", 0, Format.R32G32_Float, 12, 0, InputClassification.PerVertexData, 0),
                };

                // Describe and create the graphics pipeline state object (PSO).
                var psoDesc = new GraphicsPipelineStateDescription
                {
                    InputLayout = new InputLayoutDescription(inputElementDescs),
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
                    SampleDescription = new SampleDescription(1, 0),  //This is the default value anyway...
                };
                mPipelineState = mDevice.CreateGraphicsPipelineState(psoDesc);
            }

            // Create the command list.
            mCommandList = mDevice.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, mCommandAllocator, mPipelineState);

            // Create the vertex buffer.
            {
                var modelLoader = XModelLoader.Create3(new GraphicsDevice(mDevice), @"..\..\..\Mutiny\Models\cannon_boss.X");
                (ID3D12Resource IndexBuffer, ID3D12Resource VertexBuffer, IEnumerable<ShaderResourceView> ShaderResourceViews, Model Model) firstMesh
                    = System.Threading.Tasks.Task.Run(() => modelLoader.GetFlatShadedMeshesAsync(@"..\..\..\Mutiny", false)).Result.First();

                mVertexBuffer = firstMesh.VertexBuffer;
                mVertexBufferView = firstMesh.Model.Meshes[0].MeshDraw.VertexBufferViews![0];

                mIndexBuffer = firstMesh.IndexBuffer;
                mIndexBufferView = firstMesh.Model.Meshes[0].MeshDraw.IndexBufferView;

                //// Define the geometry for a triangle.
                //var triangleVertices = new[]
                //{
                //    new Vertex { Position = new Vector3(0.0f, 0.25f * mAspectRatio, 0.0f), TexCoord = new Vector2(0.5f, 0.0f), },
                //    new Vertex { Position = new Vector3(0.25f, -0.25f * mAspectRatio, 0.0f), TexCoord = new Vector2(1.0f, 1.0f), },
                //    new Vertex { Position = new Vector3(-0.25f, -0.25f * mAspectRatio, 0.0f), TexCoord = new Vector2(0.0f, 1.0f), },
                //};

                //int vertexBufferSize = triangleVertices.Length * Unsafe.SizeOf<Vertex>();

                //// Note: using upload heaps to transfer static data like vert buffers is not 
                //// recommended. Every time the GPU needs it, the upload heap will be marshalled 
                //// over. Please read up on Default Heap usage. An upload heap is used here for 
                //// code simplicity and because there are very few verts to actually transfer.
                //mVertexBuffer = mDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                //                                                ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead, null);

                //// Copy the triangle data to the vertex buffer.
                //mVertexBuffer.SetData(triangleVertices);

                //// Initialize the vertex buffer view.
                //mVertexBufferView.BufferLocation = mVertexBuffer.GPUVirtualAddress;
                //mVertexBufferView.StrideInBytes = Unsafe.SizeOf<Vertex>();
                //mVertexBufferView.SizeInBytes = vertexBufferSize;
            }


            // Note: ComPtr's are CPU objects but this resource needs to stay in scope until
            // the command list that references it has finished executing on the GPU.
            // We will flush the GPU at the end of this method to ensure the resource is not
            // prematurely destroyed.
            ID3D12Resource textureUploadHeap;

            // Create the texture.
            {
                // Describe and create a Texture2D.
                ResourceDescription textureDesc;
                var textureFile = new FileInfo(@"C:\Users\samne\Source\Repos\Sohra\DirectX-Graphics-Samples\HelloTriangle\Mutiny\Textures\CannonBoss_tex.jpg");
                if (textureFile.Exists) {
                    using (FileStream stream = File.OpenRead(textureFile.FullName)) {
                        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        var firstFrame = decoder.Frames.First();

                        var format = firstFrame.Format.BitsPerPixel == 32
                                   ? Format.R8G8B8A8_UNorm
                                   : Format.D24_UNorm_S8_UInt; //Used for a depth stencil, for a 24bit image consider Format.R8G8B8_UNorm
                        var pixelSizeInBytes = firstFrame.Format.BitsPerPixel / 8;
                        var stride = firstFrame.PixelWidth * pixelSizeInBytes;
                        var pixels = new byte[firstFrame.PixelHeight * stride];
                        firstFrame.CopyPixels(pixels, stride, 0);

                        textureDesc = ResourceDescription.Texture2D(format, (uint)firstFrame.PixelWidth, (uint)firstFrame.PixelHeight, 1, 1, 1, 0, ResourceFlags.None);
                        mTexture = mDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None,
                                                                   textureDesc, ResourceStates.CopyDest, null);

                        var uploadBufferSize = mDevice.GetRequiredIntermediateSize(mTexture, 0, 1);

                        // Create the GPU upload buffer.
                        textureUploadHeap = mDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                                            ResourceDescription.Buffer(uploadBufferSize), ResourceStates.GenericRead, null);

                        Span<byte> texture = pixels.AsSpan();

                        mDevice.UpdateSubresource(mCommandList, mTexture, textureUploadHeap, 0, 0, texture);
                    }
                }
                else {
                    textureDesc = ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, TextureWidth, TextureHeight, 1, 1, 1, 0, ResourceFlags.None);
                    mTexture = mDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None,
                                                               textureDesc, ResourceStates.CopyDest, null);

                    var uploadBufferSize = mDevice.GetRequiredIntermediateSize(mTexture, 0, 1);

                    // Create the GPU upload buffer.
                    textureUploadHeap = mDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                                        ResourceDescription.Buffer(uploadBufferSize), ResourceStates.GenericRead, null);
                    //Upload heap must use initial resource state of GenericRead: https://docs.microsoft.com/en-us/windows/win32/api/d3d12/nf-d3d12-id3d12device-createcommittedresource
                    //Default heap cannot provide CPU access - I guess that's why the intermediate heap is used?  https://docs.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_heap_type
                    //textureUploadHeap = mDevice.CreateCommittedResource(new HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0), HeapFlags.None,
                    //                                                    mTexture.Description, ResourceStates.CopyDestination);

                    // Copy data to the intermediate upload heap and then schedule a copy 
                    // from the upload heap to the Texture2D.
                    Span<byte> texture = GenerateTextureData();

                    mDevice.UpdateSubresource(mCommandList, mTexture, textureUploadHeap, 0, 0, texture);
                }

                //NOTE: This is not required if using a copy queue, see MJP comment at https://www.gamedev.net/forums/topic/704025-use-texture2darray-in-d3d12/
                mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mTexture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource));

                // Describe and create a SRV for the texture.
                var srvDesc = new ShaderResourceViewDescription
                {
                    Shader4ComponentMapping = ShaderComponentMapping.DefaultComponentMapping(), //D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,  //i.e. default 1:1 mapping
                    Format = textureDesc.Format,
                    ViewDimension = ShaderResourceViewDimension.Texture2D,
                };
                srvDesc.Texture2D.MipLevels = 1;
                mDevice.CreateShaderResourceView(mTexture, srvDesc, mSrvHeap.GetCPUDescriptorHandleForHeapStart());
            }

            // Close the command list and execute it to begin the initial GPU setup.
            mCommandList.Close();
            mCommandQueue.ExecuteCommandList(mCommandList);

            // Create synchronization objects and wait until assets have been uploaded to the GPU.
            {
                mFence = mDevice.CreateFence(0, FenceFlags.None);
                mFenceValue = 1;

                // Create an event handle to use for frame synchronization.
                mFenceEvent = new ManualResetEvent(false);

                // Wait for the command list to execute; we are reusing the same command 
                // list in our main loop but for now, we just want to wait for setup to 
                // complete before continuing.
                WaitForPreviousFrame();
            }
        }

        /// <summary>
        /// Generate a simple black and white checkerboard texture.
        /// </summary>
        /// <returns></returns>
        Span<byte> GenerateTextureData()
        {
            var rowPitch = TextureWidth * TexturePixelSize;
            var cellPitch = rowPitch >> 3;        // The width of a cell in the checkboard texture.
            var cellHeight = TextureWidth >> 3;    // The height of a cell in the checkerboard texture.
            var textureSize = rowPitch * TextureHeight;

            var data = new byte[textureSize];
            for (uint n = 0; n < textureSize; n += TexturePixelSize)
            {
                var x = n % rowPitch;
                var y = n / rowPitch;
                var i = x / cellPitch;
                var j = y / cellHeight;

                if (i % 2 == j % 2)
                {
                    data[n] = 0x00;        // R
                    data[n + 1] = 0x00;    // G
                    data[n + 2] = 0x00;    // B
                    data[n + 3] = 0xff;    // A
                }
                else
                {
                    data[n] = 0xff;        // R
                    data[n + 1] = 0xff;    // G
                    data[n + 2] = 0xff;    // B
                    data[n + 3] = 0xff;    // A
                }
            }

            return data.AsSpan();
        }

        void PopulateCommandList()
        {
            // Command list allocators can only be reset when the associated 
            // command lists have finished execution on the GPU; apps should use 
            // fences to determine GPU execution progress.
            mCommandAllocator.Reset();

            // However, when ExecuteCommandList() is called on a particular command 
            // list, that command list can then be reset at any time and must be before 
            // re-recording.
            mCommandList.Reset(mCommandAllocator, mPipelineState);

            // Set necessary state.
            mCommandList.SetGraphicsRootSignature(mRootSignature);
            mCommandList.SetDescriptorHeaps(1, new[] { mSrvHeap });
            mCommandList.SetGraphicsRootDescriptorTable(0, mSrvHeap.GetGPUDescriptorHandleForHeapStart());
            mCommandList.RSSetViewports(mViewport);
            mCommandList.RSSetScissorRects(mScissorRect);

            // Indicate that the back buffer will be used as a render target.
            mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mRenderTargets[mFrameIndex], ResourceStates.Present, ResourceStates.RenderTarget));

            CpuDescriptorHandle rtvHandle = mRtvHeap.GetCPUDescriptorHandleForHeapStart() + mFrameIndex * mRtvDescriptorSize;
            mCommandList.OMSetRenderTargets(rtvHandle, null);

            // Record commands.
            var clearColor = new Vortice.Mathematics.Color(0, Convert.ToInt32(0.2f * 255.0f), Convert.ToInt32(0.4f * 255.0f), 255);
            mCommandList.ClearRenderTargetView(rtvHandle, clearColor);
            mCommandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            mCommandList.IASetIndexBuffer(mIndexBufferView);
            mCommandList.IASetVertexBuffers(0, mVertexBufferView);
            if (mIndexBufferView != null) {
                mCommandList.DrawIndexedInstanced(mIndexBufferView.Value.SizeInBytes / (mIndexBufferView.Value.Format.GetBitsPerPixel() >> 3), 1, 0, 0, 0);
            }
            else {
                mCommandList.DrawInstanced(mVertexBufferView.SizeInBytes / mVertexBufferView.StrideInBytes, 1, 0, 0);
            }

            // Indicate that the back buffer will now be used to present.
            mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mRenderTargets[mFrameIndex], ResourceStates.RenderTarget, ResourceStates.Present));

            mCommandList.Close();
        }

        void WaitForPreviousFrame()
        {
            // WAITING FOR THE FRAME TO COMPLETE BEFORE CONTINUING IS NOT BEST PRACTICE.
            // This is code implemented as such for simplicity. The D3D12HelloFrameBuffering
            // sample illustrates how to use fences for efficient resource usage and to
            // maximize GPU utilization.

            // Signal and increment the fence value.
            ulong fence = mFenceValue;
            mCommandQueue.Signal(mFence, fence);
            mFenceValue++;

            // Wait until the previous frame is finished.
            if (mFence.CompletedValue < fence)
            {
                mFenceEvent.Reset();
                mFence.SetEventOnCompletion(fence, mFenceEvent).CheckError();
                mFenceEvent.WaitOne();
            }

            mFrameIndex = mSwapChain.CurrentBackBufferIndex;
        }

    }
}
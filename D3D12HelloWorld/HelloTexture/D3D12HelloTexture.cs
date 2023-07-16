using DirectX12GameEngine.Shaders;
using SharpGen.Runtime;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
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
            this.FormClosing += (object sender, FormClosingEventArgs e) => OnDestroy();
        }

        private void HandleCompositionTarget_Rendering(object sender, EventArgs e)
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
                Result debugResult = D3D12.D3D12GetDebugInterface(out ID3D12Debug debugController);

                if (debugResult.Success)
                {
                    ID3D12Debug1 debug = debugController.QueryInterface<ID3D12Debug1>();

                    debug.EnableDebugLayer();

                    // Enable additional debug layers.
                    dxgiFactoryDebugMode = true; //dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG; //0x01
                }

            }
#endif

            DXGI.CreateDXGIFactory2(dxgiFactoryDebugMode, out IDXGIFactory4 factory);

            if (mUseWarpDevice)
            {
                Result warpResult = factory.EnumWarpAdapter(out IDXGIAdapter warpAdapter);
                if (warpResult.Failure)
                    throw new COMException("EnumWarpAdaptor creation failed", warpResult.Code);

                mDevice = D3D12.D3D12CreateDevice<ID3D12Device>(warpAdapter, Vortice.Direct3D.FeatureLevel.Level_11_0);
            }
            else
            {
                IDXGIAdapter1 hardwareAdapter = null;
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
            using IDXGISwapChain1 swapChain = factory.CreateSwapChainForHwnd(mCommandQueue, base.Handle, swapChainDesc);
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
                /*//Shader Model 5.x- (FXC Compiler)
                Result vertexShaderOperation = Vortice.D3DCompiler.Compiler.CompileFromFile("HelloTextureShaders.hlsl", "VSMain", "vs_5_0", out var vertexShaderBlob, out var vertexShaderErrorBlob);
                Result pixelShaderOperation = Vortice.D3DCompiler.Compiler.CompileFromFile("HelloTextureShaders.hlsl", "PSMain", "ps_5_0", out var pixelShaderBlob, out var pixelShaderErrorBlob);
                if (vertexShaderOperation.Failure)
                    throw new COMException($"FXC Compiler Error on Vertex Shader: {vertexShaderErrorBlob.ConvertToString()}", vertexShaderOperation.Code);
                if (pixelShaderOperation.Failure)
                    throw new COMException($"FXC Compiler Error on Pixel Shader: {pixelShaderErrorBlob.ConvertToString()}", pixelShaderOperation.Code);
                var vertexShader = new ShaderBytecode(vertexShaderBlob.GetBytes());
                var pixelShader = new ShaderBytecode(pixelShaderBlob.GetBytes());//*/


                /*//Shader Model 6+ (DXC Compiler)
                //Compile with DXC, this output.s a C header file with the byte code in a constant specified by /Vn i.e. g_HelloTexture_VS in the below case
                //& "$([Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))\Windows Kits\10\bin\10.0.19041.0\x86\dxc.exe" /Zi /E"VSMain" /Vn"g_HelloTexture_VS" /Tvs_6_0 /Fh"HelloTextureShaders.hlsl.h" /nologo HelloTextureShaders.hlsl
                //Alternatively, raw output can be used 
                //& "$([Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))\Windows Kits\10\bin\10.0.19041.0\x86\dxc.exe" /Zi /E"VSMain" /Tvs_6_0 /Fo"HelloTextureShaders.hlsl.fo" /nologo HelloTextureShaders.hlsl
                var options = new Vortice.Dxc.DxcCompilerOptions { ShaderModel = Vortice.Dxc.DxcShaderModel.Model6_0, EnableDebugInfo = true, };
                Vortice.Dxc.IDxcOperationResult vertexShaderOperation = Vortice.Dxc.DxcCompiler.Compile(Vortice.Dxc.DxcShaderStage.VertexShader, "HelloTextureShaders.hlsl", "VSMain", null, options);
                Vortice.Dxc.IDxcOperationResult pixelShaderOperation = Vortice.Dxc.DxcCompiler.Compile(Vortice.Dxc.DxcShaderStage.PixelShader, "HelloTextureShaders.hlsl", "PSMain", null, options);
                var status = vertexShaderOperation.GetStatus(); //0x80004005 AKA -2147467259
                ////Throws COMException: 0xA8F28C30 when debug off
                // When debug on, vertexShaderOperation.GetErrors().GetBufferSize() = 311, and GetBufferPointer returns a value too (both unlike GetResult()), however using this to create shaderbytecode still throws InvalidProgramException
                var errors2 = vertexShaderOperation.GetErrors().GetEncoding(out bool unknown2, out uint codePage2);  //False and 0
                Vortice.Dxc.IDxcBlob vertexShaderResult = vertexShaderOperation.GetResult();
                //Throws:
                //System.InvalidProgramException
                //  HResult = 0x8013153A
                //  Message = The JIT compiler encountered invalid IL code or an internal limitation.
                //    Source=Vortice.DirectX
                //    StackTrace:
                //   at Vortice.Interop.Read[T](IntPtr source, T[] values)
                //   at Vortice.Direct3D12.ShaderBytecode..ctor(IntPtr bytecode, PointerSize length)
                //   at D3D12HelloWorld.MemoryExtensions.AsShaderBytecode(IDxcBlob source) in \HelloTriangle\MemoryExtensions.cs:line 15
                //   at D3D12HelloWorld.HelloTexture.D3D12HelloTexture.LoadAssets() in \HelloTriangle\HelloTexture\D3D12HelloTexture.cs:line 325
                //   at D3D12HelloWorld.HelloTexture.D3D12HelloTexture.OnInit() in \HelloTriangle\HelloTexture\D3D12HelloTexture.cs:line 108
                //   at D3D12HelloWorld.HelloTexture.D3D12HelloTexture..ctor(UInt32 width, UInt32 height, String name) in \HelloTriangle\HelloTexture\D3D12HelloTexture.cs:line 89
                //   at D3D12HelloWorld.Program.Main() in \HelloTriangle\Program.cs:line 19
                var vertexShader = vertexShaderResult.AsMemory();

                status = pixelShaderOperation.GetStatus();
                var errors = pixelShaderOperation.GetErrors().GetEncoding(out bool unknown, out uint codePage);
                Vortice.Dxc.IDxcBlob pixelShaderResult = pixelShaderOperation.GetResult();
                var pixelShader = pixelShaderResult.AsMemory();
                //*/

                // Compile .NET to HLSL
                var shader = new Shaders();
                var shaderGenerator = new ShaderGenerator(shader);
                ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                ReadOnlyMemory<byte> vertexShader = ShaderCompiler.Compile(ShaderStage.VertexShader, result.ShaderSource, nameof(shader.VSMain));
                ReadOnlyMemory<byte> pixelShader = ShaderCompiler.Compile(ShaderStage.PixelShader, result.ShaderSource, nameof(shader.PSMain));
                //*/

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
                    BlendState = BlendDescription.Opaque,  //Nothing seems to correspond to CD3DX12_BLEND_DESC(D3D12_DEFAULT)
                    //BlendState = BlendDescription.NonPremultiplied, //i.e. new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha),
                    //DepthStencilState = DepthStencilDescription.None,  //Default value is DepthStencilDescription.Default
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
                // Define the geometry for a triangle.
                var triangleVertices = new[]
                {
                    new Vertex { Position = new Vector3(0.0f, 0.25f * mAspectRatio, 0.0f), TexCoord = new Vector2(0.5f, 0.0f), },
                    new Vertex { Position = new Vector3(0.25f, -0.25f * mAspectRatio, 0.0f), TexCoord = new Vector2(1.0f, 1.0f), },
                    new Vertex { Position = new Vector3(-0.25f, -0.25f * mAspectRatio, 0.0f), TexCoord = new Vector2(0.0f, 1.0f), },
                };

                int vertexBufferSize = triangleVertices.Length * Unsafe.SizeOf<Vertex>();

                // Note: using upload heaps to transfer static data like vert buffers is not 
                // recommended. Every time the GPU needs it, the upload heap will be marshalled 
                // over. Please read up on Default Heap usage. An upload heap is used here for 
                // code simplicity and because there are very few verts to actually transfer.
                mVertexBuffer = mDevice.CreateCommittedResource(HeapProperties.UploadHeapProperties, HeapFlags.None,
                                                                ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead, null);

                // Copy the triangle data to the vertex buffer.
                mVertexBuffer.SetData(triangleVertices);

                // Initialize the vertex buffer view.
                mVertexBufferView.BufferLocation = mVertexBuffer.GPUVirtualAddress;
                mVertexBufferView.StrideInBytes = Unsafe.SizeOf<Vertex>();
                mVertexBufferView.SizeInBytes = vertexBufferSize;
            }


            // Note: ComPtr's are CPU objects but this resource needs to stay in scope until
            // the command list that references it has finished executing on the GPU.
            // We will flush the GPU at the end of this method to ensure the resource is not
            // prematurely destroyed.
            ID3D12Resource textureUploadHeap;

            // Create the texture.
            {
                // Describe and create a Texture2D.
                var textureDesc = ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, TextureWidth, TextureHeight, 1, 1, 1, 0, ResourceFlags.None);
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

                /*DX12GE Texture.Create2D<T>(GD d, Span<T> data, width, height, format... etc)
                  Creates resource with default heap properties, and no resource flags - so not exactly the same as mTexture, or textureUploadHeap...
                  Dimension is Texture2D, same as textureDesc
                  Then it calls SetData<T>(Span<T>, int offset = 0) which for Texture2D dimensions, creates an "upload resource" with HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0), no flags, ResourceState CopyDestination
                  Then it calls WriteToSubresource on this... creates a copy command list, calls copyCommandList.CopyResource(uploadBuffer, this) being the texture resource, then it calls flush on that commandlist
                  The C++ code didn't take this Texture2D-specific pathway, treating it more like a generic buffer...  For compatibility with the HLSL shader, perhaps ought to copy that...
                  So the DX12GE equivalent would be GraphicsResource.CreateBuffer<T>(GD d, Span<T> d, resourceFFlags, HeapType = default)
                  That would CreateCommittedResource with Dimension Buffer by default, on the default heap.  If we overrode that to UploadHeap then it would set the ResourceState to GenericRead, but otherwise it is 0, Common
                  Then it calls SetData and this time because of the Buffer dimension, and if on the default heap (yes) it would create another buffer on the upload heap, just like textureUploadHeap
                //Then it create s a new copy commandlist, calls CopyBufferRegion (as opposed to CopyResource like the Texture case did), then flush...
                So only difference with C++ and the DX12GE buffer version is the ResourceState of the mTexture - they set CopyDestination DX12GE uses Common (0)...
                And then the copy orchestration, C++ sets ResourceBarrier on CopyDestination (corresponding to what they set the initial state to) until PixelShaderResource..
                //It does this on its original command list instead of specifically creating a new copy command list...... later it calls close and ExecuteCommandLists
                //which should accomplish the same as Flush..
                */
                ////**************************************************************** THIS DOES NOT CRASH BUT RENDERS ALL BLACK **************************************************
                //Incidentally, this is exactly what the Span<T> overload of WriteToSubresource<T> (unsafe fixed, GetPinnableReference)
                //unsafe
                //{
                //    fixed (byte* ptr = &texture.GetPinnableReference())
                //    {
                //        var textureData = new SubresourceInfo
                //        {
                //            Offset = (long)ptr,
                //            RowPitch = TextureWidth * TexturePixelSize,
                //        };
                //        textureData.DepthPitch = textureData.RowPitch * TextureHeight;

                //        mDevice.UpdateSubResources(mCommandList, mTexture, textureUploadHeap, 0, 0, new[] { textureData });
                //    }
                //}
                ////**************************************************************** THIS DOES NOT CRASH BUT RENDERS ALL BLACK **************************************************

                mDevice.UpdateSubresource(mCommandList, mTexture, textureUploadHeap, 0, 0, texture);

                ////Try as I might, I can't get the original C++ code to create a texture to work, so fuck it, DX12GE buffer method:
                //IntPtr mappedResource = textureUploadHeap.Map(0);
                //texture.CopyTo(mappedResource);
                //textureUploadHeap.Unmap(0);

                ////...and this doesn't work either...
                //var commandQueue = mDevice.CreateCommandQueue(new CommandQueueDescription(CommandListType.Copy));
                //var copyCommandAllocator = mDevice.CreateCommandAllocator(CommandListType.Copy);
                //var copyCommandList = mDevice.CreateCommandList(0, CommandListType.Copy, copyCommandAllocator, null);
                //copyCommandList.CopyBufferRegion(textureUploadHeap, 0, mTexture, 0, texture.Length * Unsafe.SizeOf<byte>());
                //copyCommandList.Close();  //But now calling this throws a SharpGen.Runtime.SharpGenException: HRESULT: [0x80070057], Module:[General], ApiCode:[E_INVALIDARG/ Invalid Arguments], Message: The parameter is incorrect.
                ////Why doesn't anything work around this shithole?
                //commandQueue.ExecuteCommandLists(copyCommandList);  //Fall through to call this further down
                ////NativeCommandQueue.Signal(Fence, fenceValue);
                ////blah...
                //Fuck me. That doesn't work.  Using the existing command list doesn't work (it just fails same error down on ~L453 where it is closed, and the debug layer tells me fucking nothing)
                //dropping the resource barrier, and going to the DX12GE initial resource state doesn't work...
                //
                //mCommandList.CopyBufferRegion(textureUploadHeap, 0, mTexture, 0, texture.Length * Unsafe.SizeOf<byte>());

                ////**************************************************************** THIS DOES NOT CRASH BUT RENDERS ALL BLACK **************************************************
                //var textureDesc = ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, TextureWidth, TextureHeight, 1, 1, 1, 0, ResourceFlags.None);
                //mTexture = mDevice.CreateCommittedResource(HeapProperties.DefaultHeapProperties, HeapFlags.None,
                //                                           textureDesc, ResourceStates.CopyDestination, null);
                //// Create the GPU upload buffer.
                //textureUploadHeap = mDevice.CreateCommittedResource(new HeapProperties(CpuPageProperty.WriteBack, MemoryPool.L0), HeapFlags.None,
                //                                                    mTexture.Description, ResourceStates.CopyDestination);
                //textureUploadHeap.WriteToSubresource(0, texture, TextureWidth * TexturePixelSize, TextureWidth * TexturePixelSize * TextureHeight);
                ////var commandQueue = mDevice.CreateCommandQueue(new CommandQueueDescription(CommandListType.Copy));
                ////var copyCommandAllocator = mDevice.CreateCommandAllocator(CommandListType.Copy);
                ////var copyCommandList = mDevice.CreateCommandList(0, CommandListType.Copy, copyCommandAllocator, null);
                ////copyCommandList.CopyResource(textureUploadHeap, mTexture);
                //mCommandList.CopyResource(textureUploadHeap, mTexture);
                ////**************************************************************** THIS DOES NOT CRASH BUT RENDERS ALL BLACK **************************************************

                //NOTE: This is not required if using a copy queue, see MJP comment at https://www.gamedev.net/forums/topic/704025-use-texture2darray-in-d3d12/
                mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mTexture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource));

                //copyCommandList.Close();  //But now calling this throws a SharpGen.Runtime.SharpGenException: HRESULT: [0x80070057], Module:[General], ApiCode:[E_INVALIDARG/ Invalid Arguments], Message: The parameter is incorrect.
                //commandQueue.ExecuteCommandList(copyCommandList);


                /*System.AccessViolationException
  HResult=0x80004003
  Message=Attempted to read or write protected memory. This is often an indication that other memory is corrupt.
  Source=<Cannot evaluate the exception source>
  StackTrace:
<Cannot evaluate the exception stack trace>
*/

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
            mCommandList.IASetVertexBuffers(0, mVertexBufferView);
            mCommandList.DrawInstanced(3, 1, 0, 0);

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
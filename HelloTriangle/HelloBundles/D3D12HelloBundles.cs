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

namespace D3D12HelloWorld.HelloBundles {
    /// <summary>
    /// https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/Desktop/D3D12HelloWorld/src/HelloBundles/D3D12HelloBundles.cpp
    /// </summary>
    public partial class D3D12HelloBundles : Form
    {
        const int FrameCount = 2;

        struct Vertex
        {
            public Vector3 Position;
            public Color4 Colour;
        };

        //DXSample - Viewport dimensions
        float mAspectRatio;

        //DXSample - Adapter info
        bool mUseWarpDevice = false;

        // Pipeline objects.
        Viewport mViewport;
        RawRect mScissorRect;
        IDXGISwapChain3 mSwapChain;
        ID3D12Device mDevice;
        readonly ID3D12Resource[] mRenderTargets;
        ID3D12CommandAllocator mCommandAllocator;
        ID3D12CommandAllocator mBundleAllocator;
        ID3D12CommandQueue mCommandQueue;
        ID3D12RootSignature mRootSignature;
        ID3D12DescriptorHeap mRtvHeap;
        ID3D12PipelineState mPipelineState;
        ID3D12GraphicsCommandList mCommandList;
        ID3D12GraphicsCommandList mBundle;
        int mRtvDescriptorSize;

        // App resources.
        ID3D12Resource mVertexBuffer;
        VertexBufferView mVertexBufferView;

        // Synchronization objects.
        int mFrameIndex;
        ManualResetEvent mFenceEvent;
        ID3D12Fence mFence;
        ulong mFenceValue;

        //DX12GE - GameBase
        private readonly object mTickLock = new object();

        public D3D12HelloBundles() : this(1200, 900, string.Empty)
        {
        }

        public D3D12HelloBundles(uint width, uint height, string name)
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
            mCommandQueue.Name = "Direct Queue";

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
            mBundleAllocator = mDevice.CreateCommandAllocator(CommandListType.Bundle);
        }

        /// <summary>
        /// Load the sample assets
        /// </summary>
        void LoadAssets()
        {
            // Create an empty root signature.
            {
                var rootSignatureDesc = new RootSignatureDescription1
                {
                    Flags = RootSignatureFlags.AllowInputAssemblerInputLayout,
                };
                mRootSignature = mDevice.CreateRootSignature(rootSignatureDesc);
            }

            // Create the pipeline state, which includes compiling and loading shaders.
            {
                var shader = new HelloTriangle.Shaders();
                var shaderGenerator = new ShaderGenerator(shader);
                ShaderGeneratorResult result = shaderGenerator.GenerateShader();
                ReadOnlyMemory<byte> vertexShader = ShaderCompiler.Compile(ShaderStage.VertexShader, result.ShaderSource, nameof(shader.VSMain));
                ReadOnlyMemory<byte> pixelShader = ShaderCompiler.Compile(ShaderStage.PixelShader, result.ShaderSource, nameof(shader.PSMain));


                // Define the vertex input layout.
                //NOTE: The HLSL Semantic names here must match the ShaderTypeAttribute.TypeName associated with the ShaderSemanticAttribute associated with the 
                //      compiled Vertex Shader's Input parameters - PositionSemanticAttribute and ColorSemanticAttribute in this case per the VSInput struct
                var inputElementDescs = new[]
                {
                    new InputElementDescription("Position", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                    new InputElementDescription("Color", 0, Format.R32G32B32A32_Float, 12, 0, InputClassification.PerVertexData, 0),
                };

                // Describe and create the graphics pipeline state object (PSO).
                var psoDesc = new GraphicsPipelineStateDescription
                {
                    InputLayout = new InputLayoutDescription(inputElementDescs),
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
                    SampleDescription = new SampleDescription(1, 0),  //This is the default value anyway...
                };
                mPipelineState = mDevice.CreateGraphicsPipelineState(psoDesc);
            }

            // Create the command list.
            mCommandList = mDevice.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, mCommandAllocator, mPipelineState);

            // Command lists are created in the recording state, but there is nothing
            // to record yet. The main loop expects it to be closed, so close it now.
            mCommandList.Close();

            // Create the vertex buffer.
            {
                // Define the geometry for a triangle.
                var triangleVertices = new[]
                {
                    new Vertex { Position = new Vector3(0.0f, 0.25f * mAspectRatio, 0.0f), Colour = new Color4(1.0f, 0.0f, 0.0f, 1.0f), },
                    new Vertex { Position = new Vector3(0.25f, -0.25f * mAspectRatio, 0.0f), Colour = new Color4(0.0f, 1.0f, 0.0f, 1.0f), },
                    new Vertex { Position = new Vector3(-0.25f, -0.25f * mAspectRatio, 0.0f), Colour = new Color4(0.0f, 0.0f, 1.0f, 1.0f), },
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

            // Create and record the bundle.
            {
                mBundle = mDevice.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Bundle, mBundleAllocator, mPipelineState);
                mBundle.SetGraphicsRootSignature(mRootSignature);
                mBundle.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
                mBundle.IASetVertexBuffers(0, mVertexBufferView);
                mBundle.DrawInstanced(3, 1, 0, 0);
                mBundle.Close();
            }

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

        void PopulateCommandList() {
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
            mCommandList.RSSetViewports(mViewport);
            mCommandList.RSSetScissorRects(mScissorRect);

            // Indicate that the back buffer will be used as a render target.
            mCommandList.ResourceBarrier(ResourceBarrier.BarrierTransition(mRenderTargets[mFrameIndex], ResourceStates.Present, ResourceStates.RenderTarget));

            CpuDescriptorHandle rtvHandle = mRtvHeap.GetCPUDescriptorHandleForHeapStart() + mFrameIndex * mRtvDescriptorSize;
            mCommandList.OMSetRenderTargets(rtvHandle, null);

            // Record commands.
            var clearColor = new Vortice.Mathematics.Color(0, Convert.ToInt32(0.2f * 255.0f), Convert.ToInt32(0.4f * 255.0f), 255);
            mCommandList.ClearRenderTargetView(rtvHandle, clearColor);


            // Execute the commands stored in the bundle.
            mCommandList.ExecuteBundle(mBundle);

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

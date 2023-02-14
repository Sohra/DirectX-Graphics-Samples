using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace D3D12HelloWorld.Rendering {
    public sealed class PipelineState : IDisposable {
        public ID3D12RootSignature RootSignature { get; }

        public bool IsCompute { get; }

        internal ID3D12PipelineState NativePipelineState { get; }

        public PipelineState(GraphicsDevice device, ID3D12RootSignature rootSignature, byte[] computeShader)
            : this(device.NativeDevice, rootSignature, CreateComputePipelineStateDescription(rootSignature, computeShader)) {
        }

        internal PipelineState(ID3D12Device device, ID3D12RootSignature rootSignature, ComputePipelineStateDescription pipelineStateDescription) {
            IsCompute = true;
            RootSignature = rootSignature;
            NativePipelineState = device.CreateComputePipelineState(pipelineStateDescription);
        }

        public PipelineState(GraphicsDevice device, ID3D12RootSignature rootSignature, InputElementDescription[] inputElements, byte[] vertexShader, byte[] pixelShader, byte[]? geometryShader = null, byte[]? hullShader = null, byte[]? domainShader = null)
            : this(device.NativeDevice, rootSignature, CreateGraphicsPipelineStateDescription(device.CommandList, rootSignature, inputElements, vertexShader, pixelShader, geometryShader, hullShader, domainShader)) {
        }

        internal PipelineState(ID3D12Device device, ID3D12RootSignature rootSignature, GraphicsPipelineStateDescription pipelineStateDescription) {
            IsCompute = false;
            RootSignature = rootSignature;
            NativePipelineState = device.CreateGraphicsPipelineState(pipelineStateDescription);
        }

        public void Dispose() {
            NativePipelineState.Dispose();
        }

        private static ComputePipelineStateDescription CreateComputePipelineStateDescription(ID3D12RootSignature rootSignature, byte[] computeShader) {
            return new ComputePipelineStateDescription {
                RootSignature = rootSignature,
                ComputeShader = computeShader
            };
        }

        private static GraphicsPipelineStateDescription CreateGraphicsPipelineStateDescription(CommandList commandList, ID3D12RootSignature rootSignature, InputElementDescription[] inputElements, byte[] vertexShader, byte[] pixelShader, byte[]? geometryShader, byte[]? hullShader, byte[]? domainShader) {
            var cullNone = RasterizerDescription.CullNone;
            cullNone.FrontCounterClockwise = true;
            var alphaBlend = BlendDescription.AlphaBlend;
            var inputLayout = new InputLayoutDescription(inputElements.Select((InputElementDescription i) => Unsafe.As<InputElementDescription, Vortice.Direct3D12.InputElementDescription>(ref i)).ToArray());
            var graphicsPipelineStateDescription = new GraphicsPipelineStateDescription {
                InputLayout = inputLayout,
                RootSignature = rootSignature,
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                GeometryShader = geometryShader,
                HullShader = hullShader,
                DomainShader = domainShader,
                RasterizerState = cullNone,
                BlendState = alphaBlend,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                StreamOutput = new StreamOutputDescription()
            };
            DepthStencilView depthStencilBuffer = commandList.DepthStencilBuffer;
            if (depthStencilBuffer != null) {
                graphicsPipelineStateDescription.DepthStencilFormat = depthStencilBuffer.Resource.Description.Format;
            }

            var array = new Format[commandList.RenderTargets.Length];
            for (int j = 0; j < array.Length; j++) {
                array[j] = (Format)commandList.RenderTargets[j].Description.Format;
            }

            graphicsPipelineStateDescription.RenderTargetFormats = array;
            return graphicsPipelineStateDescription;
        }
    }
}
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace wired.Graphics {
    public sealed class PipelineState : IDisposable {
        public ID3D12RootSignature RootSignature { get; }

        public bool IsCompute { get; }

        internal ID3D12PipelineState NativePipelineState { get; }

        public PipelineState(GraphicsDevice device, ID3D12RootSignature rootSignature, byte[] computeShader, string? name = null)
            : this(device.NativeDevice, CreateComputePipelineStateDescription(rootSignature, computeShader), name) {
        }

        internal PipelineState(ID3D12Device device, ComputePipelineStateDescription pipelineStateDescription, string? name) {
            IsCompute = true;
            RootSignature = pipelineStateDescription.RootSignature ?? throw new ArgumentException($"{nameof(ComputePipelineStateDescription.RootSignature)} may not be null.", nameof(pipelineStateDescription));
            NativePipelineState = device.CreateComputePipelineState(pipelineStateDescription);
            if (!string.IsNullOrWhiteSpace(name)) {
                NativePipelineState.Name = name;
            }
        }

        public PipelineState(GraphicsDevice device, ID3D12RootSignature rootSignature, InputElementDescription[] inputElements, byte[] vertexShader, byte[] pixelShader, byte[]? geometryShader = null, byte[]? hullShader = null, byte[]? domainShader = null, string? name = null)
            : this(device.NativeDevice, CreateGraphicsPipelineStateDescription(device.CommandList, rootSignature, inputElements, vertexShader, pixelShader, geometryShader, hullShader, domainShader), name) {
        }

        internal PipelineState(ID3D12Device device, GraphicsPipelineStateDescription pipelineStateDescription, string? name) {
            IsCompute = false;
            RootSignature = pipelineStateDescription.RootSignature ?? throw new ArgumentException($"{nameof(ComputePipelineStateDescription.RootSignature)} may not be null.", nameof(pipelineStateDescription));
            NativePipelineState = device.CreateGraphicsPipelineState(pipelineStateDescription);
            if (!string.IsNullOrWhiteSpace(name)) {
                NativePipelineState.Name = name;
            }
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
            var graphicsPipelineStateDescription = new GraphicsPipelineStateDescription {
                InputLayout = new InputLayoutDescription(inputElements),
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
            DepthStencilView? depthStencilBuffer = commandList.DepthStencilBuffer;
            if (depthStencilBuffer != null) {
                graphicsPipelineStateDescription.DepthStencilFormat = depthStencilBuffer.Resource.Description.Format;
            }

            var array = new Format[commandList.RenderTargets.Length];
            for (int j = 0; j < array.Length; j++) {
                array[j] = commandList.RenderTargets[j].Resource.Description.Format;
            }

            graphicsPipelineStateDescription.RenderTargetFormats = array;
            return graphicsPipelineStateDescription;
        }
    }
}
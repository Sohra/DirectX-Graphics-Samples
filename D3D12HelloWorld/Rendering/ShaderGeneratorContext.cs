using DirectX12GameEngine.Shaders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vortice.Direct3D12;

namespace D3D12HelloWorld.Rendering {
    public class ShaderGeneratorContext {
        readonly GraphicsDevice mDevice;
        readonly ShaderGeneratorSettings mSettings;

        public IShader? Shader { get; private set; }

        public int ConstantBufferViewRegisterCount { get; set; }

        public int ShaderResourceViewRegisterCount { get; set; }

        public int UnorderedAccessViewRegisterCount { get; set; }

        public int SamplerRegisterCount { get; set; }

        public IList<RootParameter1> RootParameters { get; } = new List<RootParameter1>();

        public IList<ConstantBufferView> ConstantBufferViews { get; } = new List<ConstantBufferView>();

        public IList<ShaderResourceView> ShaderResourceViews { get; } = new List<ShaderResourceView>();

        public IList<UnorderedAccessView> UnorderedAccessViews { get; } = new List<UnorderedAccessView>();

        public IList<Sampler> Samplers { get; } = new List<Sampler>();

        public ShaderGeneratorContext(GraphicsDevice device, params Attribute[] entryPointAttributes)
            : this(device, new ShaderGeneratorSettings(entryPointAttributes)) {
        }

        public ShaderGeneratorContext(GraphicsDevice device, ShaderGeneratorSettings settings) {
            mDevice = device;
            mSettings = settings;
        }

        public virtual void Visit(IShader shader) {
            Shader = shader;
            shader.Accept(this);
        }

        public virtual void Clear() {
            ConstantBufferViewRegisterCount = 0;
            ShaderResourceViewRegisterCount = 0;
            UnorderedAccessViewRegisterCount = 0;
            SamplerRegisterCount = 0;
            RootParameters.Clear();
            ConstantBufferViews.Clear();
            ShaderResourceViews.Clear();
            UnorderedAccessViews.Clear();
            Samplers.Clear();
        }

        public virtual async Task<PipelineState> CreateComputePipelineStateAsync() {
            CompiledShader compiledShader = await CreateShaderAsync();
            return new PipelineState(rootSignature: CreateRootSignature(), device: mDevice, computeShader: compiledShader.Shaders["compute"]);
        }

        public async Task<PipelineState> CreateGraphicsPipelineStateAsync(InputElementDescription[] inputElements) {
            CompiledShader compiledShader = await CreateShaderAsync();
            return new PipelineState(rootSignature: CreateRootSignature(), device: mDevice, inputElements: inputElements,
                                     vertexShader: compiledShader.Shaders["vertex"],
                                     pixelShader: compiledShader.Shaders["pixel"],
                                     geometryShader: compiledShader.Shaders.ContainsKey("geometry")
                                                   ? compiledShader.Shaders["geometry"]
                                                   : null,
                                     hullShader: compiledShader.Shaders.ContainsKey("hull")
                                               ? compiledShader.Shaders["hull"]
                                               : null,
                                     domainShader: compiledShader.Shaders.ContainsKey("domain")
                                                 ? compiledShader.Shaders["domain"]
                                                 : null);
        }

        public virtual Task<CompiledShader> CreateShaderAsync() {
            if (Shader == null) {
                throw new InvalidOperationException("No shader has been visited");
            }

            var compiledShader = new CompiledShader();
            var shaderGenerator = new ShaderGenerator(Shader, mSettings);
            ShaderGeneratorResult shaderGeneratorResult = shaderGenerator.GenerateShader();
            foreach (KeyValuePair<string, string> entryPoint in shaderGeneratorResult.EntryPoints) {
                compiledShader.Shaders[entryPoint.Key] = ShaderCompiler.Compile(GetShaderStage(entryPoint.Key), shaderGeneratorResult.ShaderSource, entryPoint.Value);
            }

            return Task.FromResult(compiledShader);
        }

        public DescriptorSet? CreateShaderResourceViewDescriptorSet() {
            int num = ConstantBufferViews.Count + ShaderResourceViews.Count + UnorderedAccessViews.Count;
            if (num > 0) {
                var descriptorSet = new DescriptorSet(mDevice, num);
                descriptorSet.AddResourceViews(ConstantBufferViews);
                descriptorSet.AddResourceViews(ShaderResourceViews);
                descriptorSet.AddResourceViews(UnorderedAccessViews);
                return descriptorSet;
            }

            return null;
        }

        public DescriptorSet? CreateSamplerDescriptorSet() {
            int count = Samplers.Count;
            if (count > 0) {
                return new DescriptorSet(mDevice, Samplers);
            }

            return null;
        }

        public virtual ID3D12RootSignature CreateRootSignature() {
            var list = new List<RootParameter1>(RootParameters);
            FillShaderResourceViewRootParameters(list);
            FillSamplerRootParameters(list);
            var description = new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout, list.ToArray());
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
            description.StaticSamplers = new[] { sampler };
            return mDevice.NativeDevice.CreateRootSignature<ID3D12RootSignature>(new VersionedRootSignatureDescription(description));
        }

        private void FillShaderResourceViewRootParameters(IList<RootParameter1> rootParameters) {
            var list = new List<DescriptorRange1>();
            if (ConstantBufferViews.Any()) {
                list.Add(new DescriptorRange1(DescriptorRangeType.ConstantBufferView, ConstantBufferViews.Count, ConstantBufferViewRegisterCount));
            }

            if (ShaderResourceViews.Any()) {
                list.Add(new DescriptorRange1(DescriptorRangeType.ShaderResourceView, ShaderResourceViews.Count, ShaderResourceViewRegisterCount));
            }

            if (UnorderedAccessViews.Any()) {
                list.Add(new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, UnorderedAccessViews.Count, UnorderedAccessViewRegisterCount));
            }

            if (list.Any()) {
                rootParameters.Add(new RootParameter1(new RootDescriptorTable1(list.ToArray()), ShaderVisibility.All));
            }
        }

        private void FillSamplerRootParameters(List<RootParameter1> rootParameters) {
            if (Samplers.Any()) {
                rootParameters.Add(new RootParameter1(new RootDescriptorTable1(new DescriptorRange1(DescriptorRangeType.Sampler, Samplers.Count, SamplerRegisterCount)), ShaderVisibility.All));
            }
        }

        static ShaderStage GetShaderStage(string shader) {
            ShaderStage result = shader switch {
                "vertex" => ShaderStage.VertexShader,
                "pixel" => ShaderStage.PixelShader,
                "geometry" => ShaderStage.GeometryShader,
                "hull" => ShaderStage.HullShader,
                "domain" => ShaderStage.DomainShader,
                "compute" => ShaderStage.ComputeShader,
                _ => ShaderStage.Library,
            };

            return result;
        }
    }
}
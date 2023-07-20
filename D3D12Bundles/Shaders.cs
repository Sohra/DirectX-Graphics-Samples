using DirectX12GameEngine.Shaders;
using System.Numerics;
using wired.Rendering;

namespace D3D12Bundles {
    public struct VSInput
    {
        [PositionSemantic]
        public Vector3 Position;
        [NormalSemantic]
        public Vector3 Normal;
        [TextureCoordinateSemantic(0)]
        public Vector2 UV;
        [TangentSemantic]
        public Vector3 Tangent;
    }

    public struct MutinyVSInput {
        [PositionSemantic]
        public Vector3 Position;
        [TextureCoordinateSemantic(0)]
        public Vector2 UV;
    }

    public struct PSInput
    {
        [SystemPositionSemantic]
        public Vector4 Position;
        [TextureCoordinateSemantic(0)]
        public Vector2 UV;
    }

    public struct SamplingContext {
        public SamplerState Sampler;

        public Vector2 TextureCoordinate;
    }

    public class AltShader : IShader {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        //[ShaderResourceView(0)]  //Type already provides attribute, no need to repeat here unless overriding
        public Texture2D g_txDiffuse { get; set; }

        //[Sampler(0)]  //Type already provides attribute, no need to repeat here unless overriding.  Note this is for a static sampler, whereas SamplerStateAttribute is for dynamic samplers.
        public SamplerState g_sampler { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        /// <summary>
        /// Used to reduce Red and Blue contributions to the output color.
        /// </summary>
        /// <returns></returns>
        /// <remarks>It's important that this method is defined before any other method that calls it, since the C# to HLSL conversion does not reorder
        /// methods and in HLSL, a method must be declared before it is called, otherwise it results in: use of undeclared identifier 'GetFilter'
        /// despite being declared further down the file.</remarks>
        [ShaderMethod]
        public static Vector3 GetFilter() => new Vector3(0.25f, 1.0f, 0.25f);

        [ShaderMethod]
        [Shader("pixel")]
        public PSOutput PSMain(PSInput input) {
            Vector4 color = g_txDiffuse.Sample(g_sampler, input.UV);

            // Reduce R and B contributions to the output color.
            // Assigning this first to a variable is messing up HLSL generation, hence it must be either inlined, or returned by another ShaderMethod
            //Vector3 filter = new Vector3(0.25f, 1.0f, 0.25f);
            Vector3 filter = GetFilter();

            PSOutput result;
            result.ColorTarget = new Vector4(new Vector3(color.X, color.Y, color.Z) * filter, 1.0f);
            return result;
        }

        public void Accept(ShaderGeneratorContext context) {
            throw new NotImplementedException();
        }
    }

    public class SimpleShader : IShader {
        [ConstantBufferView(0)]
        public Matrix4x4 WorldViewProj { get; set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        //[ShaderResourceView(0)]
        public Texture2D g_txDiffuse { get; set; }

        //[Sampler(0)]
        public SamplerState g_sampler { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        [ShaderMethod]
        [Shader("pixel")]
        public PSOutput PSMain(PSInput input) {
            PSOutput result;
            result.ColorTarget = g_txDiffuse.Sample(g_sampler, input.UV);
            return result;
        }

        [ShaderMethod]
        [Shader("vertex")]
        public PSInput VSMain(VSInput input) {
            PSInput result;

            result.Position = Vector4.Transform(new Vector4(input.Position, 1.0f), WorldViewProj);
            result.UV = input.UV;

            return result;
        }

        [ShaderMethod]
        [Shader("vertex")]
        public PSInput MutinyVSMain(MutinyVSInput input) {
            PSInput result;

            result.Position = Vector4.Transform(new Vector4(input.Position, 1.0f), WorldViewProj);
            result.UV = input.UV;

            return result;
        }

        public void Accept(ShaderGeneratorContext context) {
            throw new NotImplementedException();
        }

    }
}
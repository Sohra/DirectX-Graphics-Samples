using DirectX12GameEngine.Shaders;
using System.Numerics;

namespace D3D12HelloWorld.HelloTexture
{
    public struct PSInput
    {
        [SystemPositionSemantic]
        public Vector4 Position;
        [TextureCoordinateSemantic]
        public Vector2 UV;
    }

    /// <summary>
    /// https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/UWP/D3D12HelloWorld/src/HelloTextrure/shaders.hlsl
    /// </summary>
    class Shaders
    {
        public Shaders()
        {
        }

        //public Shaders(Texture texture, bool convertToLinear = false)
        //{
        //    Texture = texture;
        //    ConvertToLinear = convertToLinear;
        //}

        //[IgnoreShaderMember]
        //public Texture Texture { get; set; }

        [IgnoreShaderMember]
        public bool ConvertToLinear { get; set; }

        [ShaderMember]
        public readonly SamplerState Sampler;

        [ShaderMember]
        public Texture2D ColorTexture { get; private set; }

        //[ShaderMethod]
        //[Shader("vertex")]
        //public PSInput VSMain(VSInput input)
        //{
        //    PSInput result;
        //    result.Position = input.Position;
        //    result.UV = input.UV;
        //    return result;
        //}

        /**/
        
        [ShaderMethod]
        public PSInput VSMain([PositionSemantic] Vector4 position, [TextureCoordinateSemantic] Vector2 uv)
        {
            PSInput result;
            result.Position = position;
            result.UV = uv;
            return result;
        }

        [ShaderMethod]
        [return: SystemTargetSemantic]
        public Vector4 PSMain(PSInput input)
        {
            return ColorTexture.Sample(Sampler, input.UV);
            //return Vector4.One;
        }
    }
}

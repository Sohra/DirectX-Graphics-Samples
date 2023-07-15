using D3D12HelloWorld.Rendering;
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
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        [ShaderMember]
        public SamplerState Sampler { get; set; }

        [ShaderMember]
        public Texture2D ColorTexture { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        [ShaderMethod]
        public PSInput VSMain([PositionSemantic] Vector4 position, [TextureCoordinateSemantic] Vector2 uv)
        {
            PSInput result;
            //With load scaling of 0.1f, further scale to suit this sample which doesn't use a camera, and translate it also
            position.X *= 0.1f;
            position.Y *= 0.1f;
            position.Z *= 0.1f;
            result.Position = position + new Vector4(0.0f, -0.5f, 1.5f, 0.0f);
            result.UV = uv;
            return result;
        }

        [ShaderMethod]
        [return: SystemTargetSemantic]
        public Vector4 PSMain(PSInput input)
        {
            return ColorTexture.Sample(Sampler, input.UV);
        }
    }
}

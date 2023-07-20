using DirectX12GameEngine.Shaders;
using System.Numerics;
using wired.Rendering;

namespace D3D12HelloWorld.Mutiny {
    public struct PSInput {
        [SystemPositionSemantic]
        public Vector4 Position;
        [TextureCoordinateSemantic]
        public Vector2 UV;
    }

    /// <summary>
    /// https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/UWP/D3D12HelloWorld/src/HelloTextrure/shaders.hlsl
    /// </summary>
    class Shaders {
        [ConstantBufferView(0)]
        public Matrix4x4 WorldViewProj { get; set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        //The type of this property specifies the appropriate attribute, no need to add another
        //[ShaderMember]
        public SamplerState Sampler { get; set; }

        //The type of this property specifies the appropriate attribute, no need to add another
        //[ShaderMember]
        public Texture2D ColorTexture { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        [ShaderMethod]
        public PSInput VSMain([PositionSemantic] Vector3 position, [TextureCoordinateSemantic] Vector2 uv) {
            PSInput result;
            ////With load scaling of 0.1f, further scale to suit this sample which doesn't use a camera, and translate it also
            //position.X *= 0.1f;
            //position.Y *= 0.1f;
            //position.Z *= 0.1f;
            //result.Position = position + new Vector4(0.0f, -0.5f, 1.5f, 0.0f);
            result.Position = Vector4.Transform(new Vector4(position, 1.0f), WorldViewProj);
            result.UV = uv;
            return result;
        }

        //[ShaderMethod]
        //[return: SystemTargetSemantic]
        //public Vector4 PSMain(PSInput input) {
        //    return ColorTexture.Sample(Sampler, input.UV);
        //}
        [ShaderMethod]
        [return: SystemTargetSemantic]
        public PSOutput PSMain(PSInput input) {
            return new PSOutput {
                ColorTarget = ColorTexture.Sample(Sampler, input.UV),
            };
        }

    }
}
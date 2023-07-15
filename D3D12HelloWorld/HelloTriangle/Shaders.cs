using DirectX12GameEngine.Shaders;
using System.Numerics;

namespace D3D12HelloWorld.HelloTriangle
{
    public struct VSInput
    {
        [PositionSemantic(0)]
        public Vector4 Position;
        [ColorSemantic(0)]
        public Vector4 Colour;  //While Vortice.Mathematics.Color4 is more appropriate on the .NET side, this is for HLSL compilation and DirectX12GameEngine.Shaders.HlslKnownTypes is setup to honour Vector4 but not Vortice.Mathematics.Color4.  We could register Color4 perhaps but it probably doesn't make a difference.  knownMethods does map .W to .w so the fact it is stored first in an array of floats shouldn't matter?  Maybe?
    }

    public struct PSInput
    {
        [SystemPositionSemantic]
        public Vector4 Position;
        [ColorSemantic(0)]
        public Vector4 Colour;
    }

    /// <summary>
    /// https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/UWP/D3D12HelloWorld/src/HelloTriangle/shaders.hlsl
    /// </summary>
    class Shaders
    {
        [ShaderMethod]
        [Shader("vertex")]
        public PSInput VSMain(VSInput input)
        {
            PSInput result;
            result.Position = input.Position;
            result.Colour = input.Colour;
            return result;
        }

        [ShaderMethod]
        [Shader("pixel")]
        public PSOutput PSMain(PSInput input)
        {
            PSOutput result;
            result.ColorTarget = input.Colour;
            return result;
        }
    }
}
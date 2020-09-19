using DirectX12GameEngine.Shaders;
using System;
using System.Windows.Forms;

namespace D3D12HelloWorld
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            //Application.Run(new HelloTriangle.D3D12HelloTriangle(1200, 900, string.Empty));
            //Application.Run(new HelloTexture.D3D12HelloTexture(1200, 900, string.Empty));
            Application.Run(new HelloFrameBuffering.D3D12HelloFrameBuffering(1200, 900, string.Empty));
        }

        internal static ShaderStage GetShaderStage(string shader) => shader switch
        {
            "vertex" => ShaderStage.VertexShader,
            "pixel" => ShaderStage.PixelShader,
            "geometry" => ShaderStage.GeometryShader,
            "hull" => ShaderStage.HullShader,
            "domain" => ShaderStage.DomainShader,
            "compute" => ShaderStage.ComputeShader,
            _ => ShaderStage.Library
        };

    }

    /// <summary>
    /// https://docs.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_shader_component_mapping
    /// https://github.com/Mayhem50/SharpMiniEngine/blob/b955ec87786278af6bc6281ea84b5ce968a3ab21/Assemblies/Core/ShaderComponentMapping.cs
    /// </summary>
    public class ShaderComponentMapping
    {

        public const int ComponentMappingMask = 0x7;

        public const int ComponentMappingShift = 3;

        public const int ComponentMappingAlwaysSetBitAvoidingZeromemMistakes = (1 << (ComponentMappingShift * 4));

        public static int ComponentMapping(int src0, int src1, int src2, int src3)
        {

            return ((((src0) & ComponentMappingMask) |
                    (((src1) & ComponentMappingMask) << ComponentMappingShift) |
                    (((src2) & ComponentMappingMask) << (ComponentMappingShift * 2)) |
                    (((src3) & ComponentMappingMask) << (ComponentMappingShift * 3)) |
                    ComponentMappingAlwaysSetBitAvoidingZeromemMistakes));
        }

        public static int DefaultComponentMapping()
        {
            return ComponentMapping(0, 1, 2, 3);
        }

        public static int ComponentMapping(int ComponentToExtract, int Mapping)
        {
            return ((Mapping >> (ComponentMappingShift * ComponentToExtract) & ComponentMappingMask));
        }
    }

}
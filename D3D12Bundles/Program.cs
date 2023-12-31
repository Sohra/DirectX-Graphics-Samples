﻿using DirectX12GameEngine.Shaders;
using Serilog;
using System.IO;
using System.Reflection;

namespace D3D12Bundles {
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            var logConfig = new LoggerConfiguration().MinimumLevel.Debug();
#if DEBUG
            logConfig.WriteTo.Debug();
#endif
            var hostAssembly = typeof(Program).GetTypeInfo().Assembly;
            var workingFolder = Path.GetDirectoryName(hostAssembly!.Location)!;
            var basePath = workingFolder;
            logConfig.WriteTo.File(Path.Combine(basePath, "log.txt"), rollingInterval: RollingInterval.Day)
                     .Enrich.FromLogContext();
            Log.Logger = logConfig.CreateLogger();  //Share this for everyone else to use

            Log.Logger.Information("Starting v{productVersion:l} in {currentDirectory}, running from {WorkingFolder}",
                                   hostAssembly!.GetName()!.Version!.ToString(3), Environment.CurrentDirectory, workingFolder);

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new D3D12Bundles(1200, 900, "D3D12Bundles", Log.Logger));
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
            return Mapping >> (ComponentMappingShift * ComponentToExtract) & ComponentMappingMask;
        }
    }

}
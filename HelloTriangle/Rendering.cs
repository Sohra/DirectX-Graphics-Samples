using DirectX12GameEngine.Shaders;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace D3D12HelloWorld.HelloTexture
{
    //Unremarking these can generate HLSL identical to the Shader Model 5 source, but even if these are indexed with 0, it still work fine so this is redundant
    //[ShaderType("SV_POSITION")]
    //public class SystemPositionSemanticAttribute : ShaderSemanticAttribute
    //{
    //    public SystemPositionSemanticAttribute()
    //    {
    //    }
    //}

    //[ShaderType("POSITION")]
    //public class PositionSemanticAttribute : ShaderSemanticAttribute
    //{
    //    public PositionSemanticAttribute()
    //    {
    //    }
    //}

    //[ShaderType("TEXCOORD")]
    //public class TextureCoordinateSemanticAttribute : ShaderSemanticAttribute
    //{
    //    public TextureCoordinateSemanticAttribute()
    //    {
    //    }
    //}

    //[ShaderType("SV_TARGET")]
    //public class SystemTargetSemanticAttribute : ShaderSemanticAttribute
    //{
    //    public SystemTargetSemanticAttribute()
    //    {
    //    }
    //}

    public class Sampler
    {
        public Sampler(ID3D12Device device) : this(device, SamplerDescription.Default)
        {
        }

        public Sampler(Sampler sampler)
        {
            //GraphicsDevice = sampler.GraphicsDevice;
            Description = sampler.Description;
            CpuDescriptorHandle = sampler.CpuDescriptorHandle;
        }

        public Sampler(ID3D12Device device, SamplerDescription description)
        {
            //GraphicsDevice = device;
            Description = description;
            //CpuDescriptorHandle = CreateSampler(device);
        }

        //public GraphicsDevice GraphicsDevice { get; }

        public SamplerDescription Description { get; }

        public IntPtr CpuDescriptorHandle { get; }

        //private IntPtr CreateSampler(ID3D12Device device)
        //{
        //    IntPtr cpuHandle = GraphicsDevice.SamplerAllocator.Allocate(1);
        //    device.CreateSampler(Description, cpuHandle.ToCpuDescriptorHandle());

        //    return cpuHandle;
        //}
    }

    internal static class DescriptorExtensions
    {
        public static CpuDescriptorHandle ToCpuDescriptorHandle(this IntPtr value) => Unsafe.As<IntPtr, CpuDescriptorHandle>(ref value);

        //public static GpuDescriptorHandle ToGpuDescriptorHandle(this long value) => Unsafe.As<long, GpuDescriptorHandle>(ref value);
    }

    [ShaderType("SamplerState")]
    [Sampler]
    public class SamplerState : Sampler
    {
        public SamplerState(Sampler sampler) : base(sampler)
        {
        }
    }

    [ShaderType("Texture2D")]
    [ShaderResourceView]
    public class Texture2D<T>// : ShaderResourceView where T : unmanaged
    {
        //public Texture2D(ShaderResourceView shaderResourceView) : base(shaderResourceView)
        //{
        //}

        public T Sample(Sampler sampler, Vector2 textureCoordinate) => throw new NotImplementedException();
    }

    [ShaderType("Texture2D")]
    [ShaderResourceView]
    public class Texture2D : Texture2D<Vector4>
    {
        //public Texture2D(ShaderResourceView shaderResourceView) : base(shaderResourceView)
        //{
        //}
    }

}

using DirectX12GameEngine.Shaders;
using System;
using System.Numerics;
using wired.Graphics;

namespace wired.Rendering {
    //internal static class DescriptorExtensions {
    //    public static CpuDescriptorHandle ToCpuDescriptorHandle(this IntPtr value) => Unsafe.As<IntPtr, CpuDescriptorHandle>(ref value);

    //    public static GpuDescriptorHandle ToGpuDescriptorHandle(this long value) => Unsafe.As<long, GpuDescriptorHandle>(ref value);
    //}

    [ShaderType("SamplerState")]
    [Sampler]
    public class SamplerState : Sampler {
        public SamplerState(Sampler sampler) : base(sampler) {
        }
    }

    [ShaderType("Texture2D")]
    [ShaderResourceView]
    public class Texture2D<T> : ShaderResourceView where T : unmanaged {
        public Texture2D(ShaderResourceView shaderResourceView) : base(shaderResourceView) {
        }

        public T Sample(Sampler sampler, Vector2 textureCoordinate) => throw new NotImplementedException("This is intentionally not implemented.");
    }

    [ShaderType("Texture2D")]
    [ShaderResourceView]
    public class Texture2D : Texture2D<Vector4> {
        public Texture2D(ShaderResourceView shaderResourceView) : base(shaderResourceView) {
        }
    }
}
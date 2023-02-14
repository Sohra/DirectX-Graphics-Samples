using DirectX12GameEngine.Shaders;

namespace D3D12HelloWorld.Rendering {
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
}
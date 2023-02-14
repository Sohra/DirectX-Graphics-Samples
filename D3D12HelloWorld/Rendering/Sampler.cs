using Vortice.Direct3D12;

namespace D3D12HelloWorld.Rendering {
    public class Sampler {
        public Sampler(ID3D12Device device) : this(device, SamplerDescription.Default) {
        }

        public Sampler(Sampler sampler) {
            //GraphicsDevice = sampler.GraphicsDevice;
            Description = sampler.Description;
            CpuDescriptorHandle = sampler.CpuDescriptorHandle;
        }

        public Sampler(ID3D12Device device, SamplerDescription description) {
            //GraphicsDevice = device;
            Description = description;
            //CpuDescriptorHandle = CreateSampler(device);
        }

        //public GraphicsDevice GraphicsDevice { get; }

        public SamplerDescription Description { get; }

        public CpuDescriptorHandle CpuDescriptorHandle { get; }

        //private IntPtr CreateSampler(ID3D12Device device)
        //{
        //    IntPtr cpuHandle = GraphicsDevice.SamplerAllocator.Allocate(1);
        //    device.CreateSampler(Description, cpuHandle.ToCpuDescriptorHandle());

        //    return cpuHandle;
        //}
    }
}
using Vortice.Direct3D12;

namespace wired.Graphics {
    public class Sampler {
        SamplerDescription mDescription;

        public Sampler(ID3D12Device device, DescriptorAllocator samplerAllocator) : this(device, samplerAllocator, SamplerDescription.Default) {
        }

        public Sampler(Sampler sampler) {
            mDescription = sampler.Description;
            CpuDescriptorHandle = sampler.CpuDescriptorHandle;
        }

        public Sampler(ID3D12Device device, DescriptorAllocator samplerAllocator, SamplerDescription description) {
            mDescription = description;
            CpuDescriptorHandle = CreateSampler(device, samplerAllocator);
        }

        public SamplerDescription Description => mDescription;
        public CpuDescriptorHandle CpuDescriptorHandle { get; }

        private CpuDescriptorHandle CreateSampler(ID3D12Device device, DescriptorAllocator samplerAllocator) {
            CpuDescriptorHandle cpuHandle = samplerAllocator.Allocate(1);
            device.CreateSampler(ref mDescription, cpuHandle);

            return cpuHandle;
        }
    }
}
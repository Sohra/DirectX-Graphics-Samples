using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.Direct3D12;

namespace wired.Graphics {
    public sealed class DescriptorSet {
        readonly ID3D12Device mDevice;
        public DescriptorHeapType DescriptorHeapType { get; }
        public int CurrentDescriptorCount { get; private set; }
        public int DescriptorCapacity { get; private set; }
        public DescriptorAllocator DescriptorAllocator { get; }
        public CpuDescriptorHandle StartCpuDescriptorHandle { get; }

        public DescriptorSet(GraphicsDevice device, IEnumerable<Sampler> samplers)
            : this(device, samplers.Count(), DescriptorHeapType.Sampler) {
            AddSamplers(samplers);
        }

        public DescriptorSet(GraphicsDevice device, int descriptorCount, DescriptorHeapType descriptorHeapType = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView) {
            if (descriptorCount < 1) {
                throw new ArgumentOutOfRangeException("descriptorCount");
            }

            mDevice = device.NativeDevice;
            DescriptorHeapType = descriptorHeapType;
            DescriptorCapacity = descriptorCount;
            DescriptorAllocator = GetDescriptorAllocator(device);
            StartCpuDescriptorHandle = DescriptorAllocator.Allocate(DescriptorCapacity);
        }

        public void AddResourceViews(IEnumerable<ResourceView> resources) {
            AddDescriptors(resources.Select((ResourceView r) => r.CpuDescriptorHandle));
        }

        public void AddResourceViews(params ResourceView[] resources) {
            AddResourceViews(resources.AsEnumerable());
        }

        public void AddSamplers(IEnumerable<Sampler> samplers) {
            AddDescriptors(samplers.Select((Sampler r) => r.CpuDescriptorHandle));
        }

        public void AddSamplers(params Sampler[] samplers) {
            AddSamplers(samplers);
        }

        private void AddDescriptors(IEnumerable<CpuDescriptorHandle> descriptors) {
            if (descriptors.Any()) {
                CpuDescriptorHandle[] array = descriptors.ToArray();
                if (CurrentDescriptorCount + array.Length > DescriptorCapacity) {
                    throw new InvalidOperationException();
                }

                int[] array2 = new int[array.Length];
                for (int i = 0; i < array2.Length; i++) {
                    array2[i] = 1;
                }

                var value = new CpuDescriptorHandle(StartCpuDescriptorHandle, CurrentDescriptorCount, DescriptorAllocator.DescriptorHandleIncrementSize);
                mDevice.CopyDescriptors(1, new CpuDescriptorHandle[1] { value, }, new int[1] { array.Length }, array.Length, array, array2, DescriptorHeapType);
                CurrentDescriptorCount += array.Length;
            }
        }

        DescriptorAllocator GetDescriptorAllocator(GraphicsDevice device) {
            DescriptorHeapType descriptorHeapType = DescriptorHeapType;

            DescriptorAllocator result = descriptorHeapType switch {
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView => device.ShaderResourceViewAllocator,
                DescriptorHeapType.Sampler => device.SamplerAllocator,
                _ => throw new NotSupportedException("This descriptor heap type is not supported."),
            };

            return result;
        }
    }
}
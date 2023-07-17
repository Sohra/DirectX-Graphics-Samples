using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.Direct3D12;

namespace wired.Graphics {
    /// <summary>
    /// Represents a set of descriptors in a descriptor heap.
    /// </summary>
    /// <remarks>
    /// A DescriptorSet is a collection of descriptors, which are data structures that describe resources to the GPU.
    /// These resources can be buffers, textures, or samplers. The DescriptorSet class provides methods for adding resource views and samplers to the set.
    /// It also keeps track of the current descriptor count and the capacity of the descriptor heap.
    /// The DescriptorSet ensures that the descriptors are arranged contiguously in the descriptor heap, even if the source descriptors are not contiguous.
    /// </remarks>
    public sealed class DescriptorSet {
        readonly ID3D12Device mDevice;
        /// <summary>
        /// Gets the type of the descriptor heap.
        /// </summary>
        public DescriptorHeapType DescriptorHeapType { get; }

        /// <summary>
        /// Gets the current number of descriptors in the set.
        /// </summary>
        public int CurrentDescriptorCount { get; private set; }

        /// <summary>
        /// Gets the maximum number of descriptors that the set can hold.
        /// </summary>
        public int DescriptorCapacity { get; private set; }

        /// <summary>
        /// Gets the descriptor allocator used by the set.
        /// </summary>
        public DescriptorAllocator DescriptorAllocator { get; }

        /// <summary>
        /// Gets the CPU descriptor handle that points to the start of the set.
        /// </summary>
        public CpuDescriptorHandle StartCpuDescriptorHandle { get; }

        /// <summary>
        /// Initializes a new instance of the DescriptorSet class with the specified samplers.
        /// </summary>
        /// <param name="device">The graphics device.</param>
        /// <param name="samplers">The samplers to add to the set.</param>
        public DescriptorSet(GraphicsDevice device, IEnumerable<Sampler> samplers)
            : this(device, samplers.Count(), DescriptorHeapType.Sampler) {
            AddSamplers(samplers);
        }

        /// <summary>
        /// Initializes a new instance of the DescriptorSet class with the specified capacity and descriptor heap type.
        /// </summary>
        /// <param name="device">The graphics device.</param>
        /// <param name="descriptorCount">The maximum number of descriptors that the set can hold.</param>
        /// <param name="descriptorHeapType">The type of the descriptor heap.</param>
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

        /// <summary>
        /// Adds the specified resource views to the set.
        /// </summary>
        /// <param name="resources">The resource views to add.</param>
        public void AddResourceViews(IEnumerable<ResourceView> resources) {
            AddDescriptors(resources.Select((ResourceView r) => r.CpuDescriptorHandle));
        }

        /// <summary>
        /// Adds the specified resource views to the set.
        /// </summary>
        /// <param name="resources">The resource views to add.</param>
        public void AddResourceViews(params ResourceView[] resources) {
            AddResourceViews(resources.AsEnumerable());
        }

        /// <summary>
        /// Adds the specified samplers to the set.
        /// </summary>
        /// <param name="samplers">The samplers to add.</param>
        public void AddSamplers(IEnumerable<Sampler> samplers) {
            AddDescriptors(samplers.Select((Sampler r) => r.CpuDescriptorHandle));
        }

        /// <summary>
        /// Adds the specified samplers to the set.
        /// </summary>
        /// <param name="samplers">The samplers to add.</param>
        public void AddSamplers(params Sampler[] samplers) {
            AddSamplers(samplers);
        }

        /// <summary>
        /// Adds a collection of descriptors to the descriptor set.
        /// </summary>
        /// <param name="descriptors">The descriptors to add to the descriptor set.</param>
        /// <exception cref="InvalidOperationException">This descriptor set lacks the necessary capacity to add the specified descriptors.</exception>
        /// <remarks>
        /// This method copies the descriptors from the provided collection into the descriptor heap associated with this descriptor set.
        /// It increments the current descriptor count of the descriptor set accordingly and checks if the descriptor set has sufficient capacity to accommodate the new descriptors.
        /// Each descriptor is copied individually, with the size of the range for each descriptor set to 1. This approach ensures that even if the source descriptors are not contiguous,
        /// they are arranged contiguously in the destination descriptor heap that this set represents.
        /// </remarks>
        void AddDescriptors(IEnumerable<CpuDescriptorHandle> descriptors) {
            if (!descriptors.Any()) {
                return;
            }

            CpuDescriptorHandle[] sourceDescriptors = descriptors.ToArray();
            if (CurrentDescriptorCount + sourceDescriptors.Length > DescriptorCapacity) {
                throw new InvalidOperationException();
            }

            int[] sourceDescriptorRangeSizes = new int[sourceDescriptors.Length];
            for (int i = 0; i < sourceDescriptorRangeSizes.Length; i++) {
                sourceDescriptorRangeSizes[i] = 1;
            }

            var destinationDescriptor = new CpuDescriptorHandle(StartCpuDescriptorHandle, CurrentDescriptorCount, DescriptorAllocator.DescriptorHandleIncrementSize);
            mDevice.CopyDescriptors(1, new CpuDescriptorHandle[1] { destinationDescriptor, }, new int[1] { sourceDescriptors.Length },
                                    sourceDescriptors.Length, sourceDescriptors, sourceDescriptorRangeSizes, DescriptorHeapType);
            CurrentDescriptorCount += sourceDescriptors.Length;
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
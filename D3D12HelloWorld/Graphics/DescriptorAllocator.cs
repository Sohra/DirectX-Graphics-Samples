using System;
using Vortice.Direct3D12;

namespace wired.Graphics {
    public sealed class DescriptorAllocator : IDisposable {
        private const int DescriptorsPerHeap = 4096;

        readonly object mAllocatorLock = new object();

        readonly DescriptorHeapDescription mDescription;

        public int CurrentDescriptorCount { get; private set; }
        public int DescriptorHandleIncrementSize { get; }
        internal ID3D12DescriptorHeap DescriptorHeap { get; }

        public DescriptorAllocator(ID3D12Device device, DescriptorHeapType descriptorHeapType, int descriptorCount = DescriptorsPerHeap, DescriptorHeapFlags descriptorHeapFlags = DescriptorHeapFlags.None) {
            if (descriptorCount < 1 || descriptorCount > DescriptorsPerHeap) {
                throw new ArgumentOutOfRangeException(nameof(descriptorCount), $"Descriptor count must be between 1 and {DescriptorsPerHeap}.");
            }

            DescriptorHandleIncrementSize = device.GetDescriptorHandleIncrementSize(descriptorHeapType);
            mDescription = new DescriptorHeapDescription(descriptorHeapType, descriptorCount, descriptorHeapFlags);
            DescriptorHeap = device.CreateDescriptorHeap(mDescription);
        }

        public CpuDescriptorHandle Allocate(int count) {
            lock (mAllocatorLock) {
                if (count < 1 || count > mDescription.DescriptorCount) {
                    throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and the total descriptor count.");
                }

                if (CurrentDescriptorCount + count > mDescription.DescriptorCount) {
                    Reset();
                }

                CpuDescriptorHandle result = DescriptorHeap.GetCPUDescriptorHandleForHeapStart().Offset(CurrentDescriptorCount, DescriptorHandleIncrementSize);
                CurrentDescriptorCount += count;
                return result;
            }
        }

        public CpuDescriptorHandle AllocateSlot(int slot) {
            lock (mAllocatorLock) {
                if (slot < 0 || slot > mDescription.DescriptorCount - 1) {
                    throw new ArgumentOutOfRangeException(nameof(slot), "Slot must be between 0 and the total descriptor count - 1.");
                }

                CpuDescriptorHandle descriptor = DescriptorHeap.GetCPUDescriptorHandleForHeapStart().Offset(slot, DescriptorHandleIncrementSize);
                return descriptor;
            }
        }

        public GpuDescriptorHandle GetGpuDescriptorHandle(CpuDescriptorHandle descriptor) {
            if (!mDescription.Flags.HasFlag(DescriptorHeapFlags.ShaderVisible)) {
                throw new InvalidOperationException();
            }

            var offset = (int)(descriptor - DescriptorHeap.GetCPUDescriptorHandleForHeapStart());
            return new GpuDescriptorHandle(DescriptorHeap.GetGPUDescriptorHandleForHeapStart(), offset);
        }

        public void Dispose() {
            DescriptorHeap.Dispose();
        }

        public void Reset() {
            CurrentDescriptorCount = 0;
        }
    }
}
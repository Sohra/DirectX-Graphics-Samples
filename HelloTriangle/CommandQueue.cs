using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D12;

namespace D3D12HelloWorld {
    class CompiledCommandList {
        private readonly ID3D12CommandAllocator mCommandAllocator;

        public CompiledCommandList(ID3D12GraphicsCommandList commandList, ID3D12CommandAllocator commandAllocator) {
            CommandList = commandList;
            mCommandAllocator = commandAllocator;
        }

        internal ID3D12CommandList CommandList { get; }
    }

    class CommandQueue : IDisposable {
        private readonly ID3D12Device mDevice;
        private readonly ID3D12CommandQueue mCommandQueue;
        private readonly ID3D12Fence mFence;
        private long mNextFenceValue;

        public CommandQueue(ID3D12Device device, CommandListType commandListType) {
            mDevice = device;
            mCommandQueue = mDevice.CreateCommandQueue(new CommandQueueDescription(commandListType));
            mFence = mDevice.CreateFence(0);
            mNextFenceValue = 1;
        }

        public void Dispose() {
            mCommandQueue.Dispose();
            mFence.Dispose();
        }

        public void ExecuteCommandLists(params CompiledCommandList[] commandLists)
            => ExecuteCommandLists(commandLists.AsEnumerable());

        public void ExecuteCommandLists(IEnumerable<CompiledCommandList> commandLists) {
            if (commandLists.Count() == 0)
                return;

            long fenceValue = ExecuteCommandListsInternal(commandLists);

            WaitForFence(mFence, fenceValue);
        }

        long ExecuteCommandListsInternal(IEnumerable<CompiledCommandList> commandLists) {
            long fenceValue = mNextFenceValue++;
            ID3D12CommandList[] nativeCommandLists = commandLists.Select(c => c.CommandList).ToArray();

            mCommandQueue.ExecuteCommandLists(nativeCommandLists);
            mCommandQueue.Signal(mFence, fenceValue);

            return fenceValue;
        }

        bool IsFenceComplete(ID3D12Fence fence, long fenceValue)
            => fence.CompletedValue >= fenceValue;

        void WaitForFence(ID3D12Fence fence, long fenceValue) {
            if (IsFenceComplete(fence, fenceValue))
                return;

            using var fenceEvent = new ManualResetEvent(false);
            fence.SetEventOnCompletion(fenceValue, fenceEvent);

            fenceEvent.WaitOne();
        }

        //To use this code, add a NuGet reference top Nito.AsyncEx.Interop.WaitHandles, and import the namespace Nito.AsyncEx.Interop
        /*Task WaitForFenceAsync(ID3D12Fence fence, long fenceValue) {
            if (IsFenceComplete(fence, fenceValue))
                return Task.CompletedTask;

            var fenceEvent = new ManualResetEvent(false);
            fence.SetEventOnCompletion(fenceValue, fenceEvent);

            return WaitHandleAsyncFactory.FromWaitHandle(fenceEvent);
        }*/
    }
}
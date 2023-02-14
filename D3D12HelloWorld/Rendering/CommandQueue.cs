using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vortice.Direct3D12;

namespace D3D12HelloWorld.Rendering {
    public class CompiledCommandList {
#pragma warning disable IDE0052 // Remove unread private members
        //Hold a reference to this, to ensure it is not disposed while we still hold a reference to the command list associated with it
        readonly ID3D12CommandAllocator mCommandAllocator;
#pragma warning restore IDE0052 // Remove unread private members

        public CompiledCommandList(ID3D12GraphicsCommandList commandList, ID3D12CommandAllocator commandAllocator) {
            CommandList = commandList;
            mCommandAllocator = commandAllocator;
        }

        internal ID3D12CommandList CommandList { get; }
    }

    public class CommandQueue : IDisposable {
        //readonly ID3D12Device mDevice;
        readonly ID3D12CommandQueue mCommandQueue;
        readonly ID3D12Fence mFence;
        ulong mNextFenceValue;

        public CommandQueue(ID3D12Device device, CommandListType commandListType, string name) {
            //mDevice = device;
            mCommandQueue = device.CreateCommandQueue(new CommandQueueDescription(commandListType));
            mCommandQueue.Name = name;
            mFence = device.CreateFence(0);
            mNextFenceValue = 1;
        }

        public void Dispose() {
            mFence.Dispose();
            mCommandQueue.Dispose();
        }

        public void ExecuteCommandLists(params CompiledCommandList[] commandLists)
            => ExecuteCommandLists(commandLists.AsEnumerable());

        public void ExecuteCommandLists(IEnumerable<CompiledCommandList> commandLists) {
            if (!commandLists.Any())
                return;

            ulong fenceValue = ExecuteCommandListsInternal(commandLists);

            WaitForFence(mFence, fenceValue);
        }

        ulong ExecuteCommandListsInternal(IEnumerable<CompiledCommandList> commandLists) {
            ulong fenceValue = mNextFenceValue++;
            ID3D12CommandList[] nativeCommandLists = commandLists.Select(c => c.CommandList).ToArray();

            mCommandQueue.ExecuteCommandLists(nativeCommandLists);
            mCommandQueue.Signal(mFence, fenceValue);

            return fenceValue;
        }

        bool IsFenceComplete(ID3D12Fence fence, ulong fenceValue)
            => fence.CompletedValue >= fenceValue;

        void WaitForFence(ID3D12Fence fence, ulong fenceValue) {
            if (IsFenceComplete(fence, fenceValue))
                return;

            Log.Logger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Waiting for {requiredFenceValue}, currently at {currentFenceValue}",
                             nameof(WaitForFence), fenceValue, fence.CompletedValue);
            using var fenceEvent = new ManualResetEvent(false);
            fence.SetEventOnCompletion(fenceValue, fenceEvent).CheckError();

            fenceEvent.WaitOne();
        }

        //To use this code, add a NuGet reference top Nito.AsyncEx.Interop.WaitHandles, and import the namespace Nito.AsyncEx.Interop
        /*Task WaitForFenceAsync(ID3D12Fence fence, long fenceValue) {
            if (IsFenceComplete(fence, fenceValue))
                return Task.CompletedTask;

            var fenceEvent = new ManualResetEvent(false);
            fence.SetEventOnCompletion(fenceValue, fenceEvent).CheckError();

            return WaitHandleAsyncFactory.FromWaitHandle(fenceEvent);
        }*/
    }
}
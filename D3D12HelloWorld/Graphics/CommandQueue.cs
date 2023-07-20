using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vortice.Direct3D12;

namespace wired.Graphics {
    public class CompiledCommandList {
#pragma warning disable IDE0052 // Remove unread private members
        //Hold a reference to this, to ensure it is not disposed while we still hold a reference to the command list associated with it
        readonly ID3D12CommandAllocator mCommandAllocator;
#pragma warning restore IDE0052 // Remove unread private members

        public CompiledCommandList(CommandList builder, ID3D12CommandAllocator commandAllocator, ID3D12GraphicsCommandList nativeCommandList) {
            Builder = builder;
            NativeCommandList = nativeCommandList;
            mCommandAllocator = commandAllocator;
        }

        internal CommandList Builder { get; }

        internal ID3D12GraphicsCommandList NativeCommandList { get; }
    }

    public class CommandQueue : IDisposable {
        //readonly ID3D12Device mDevice;
        readonly ILogger mLogger;
        readonly ID3D12CommandQueue mCommandQueue;

        // Synchronization objects.
        ManualResetEvent mFenceEvent;
        readonly ID3D12Fence mFence;
        ulong mNextFenceValue;

        /// <summary>
        /// Intended only for use by the call to IDXGIFactory2.CreateSwapChainForHwnd
        /// </summary>
        internal ID3D12CommandQueue NativeCommandQueue => mCommandQueue;

        public CommandQueue(GraphicsDevice device, CommandListType commandListType, string name) {
            //mDevice = device;
            mLogger = device.Logger;
            mCommandQueue = device.NativeDevice.CreateCommandQueue(new CommandQueueDescription(commandListType));
            mCommandQueue.Name = name;

            // Create synchronization objects and wait until assets have been uploaded to the GPU.
            mFence = device.NativeDevice.CreateFence(0);
            mNextFenceValue = 1;

            // Create an event handle to use for frame synchronization.
            mFenceEvent = new ManualResetEvent(false);
        }

        public void Dispose() {
            mFenceEvent.Dispose();
            mFence.Dispose();
            mCommandQueue.Dispose();
        }

        /// <summary>
        /// Executes a single compiled command list on the GPU without waiting for its completion.
        /// </summary>
        /// <param name="commandList">The compiled command list to execute.</param>
        internal void ExecuteCommandList(CompiledCommandList commandList)
            => mCommandQueue.ExecuteCommandList(commandList.NativeCommandList);

        /// <summary>
        /// Executes an array of compiled command lists on the GPU and waits for their completion.
        /// </summary>
        /// <param name="commandLists">The array of compiled command lists to execute.</param>
        public void ExecuteCommandLists(params CompiledCommandList[] commandLists)
            => ExecuteCommandLists(commandLists.AsEnumerable());

        /// <summary>
        /// Executes a collection of compiled command lists on the GPU and waits for their completion.
        /// </summary>
        /// <param name="commandLists">The collection of compiled command lists to execute.</param>
        public void ExecuteCommandLists(IEnumerable<CompiledCommandList> commandLists) {
            if (!commandLists.Any())
                return;

            ulong fenceValue = ExecuteCommandListsInternal(commandLists);

            WaitForFence(mFence, fenceValue);
        }

        /// <summary>
        /// Schedules a signal on the GPU command queue. This method can be used in combination with <see cref="WaitForSignal(ulong)"/> to wait for the GPU to complete work up to this point.
        /// </summary>
        /// <returns>The value that will be signaled on the GPU command queue.</returns>
        public ulong AddSignal() {
            ulong fenceValue = mNextFenceValue++;
            mCommandQueue.Signal(mFence, fenceValue).CheckError();
            return fenceValue;
        }

        /// <summary>
        /// Waits until the GPU has signaled the specified value. This method can be used to ensure that the GPU has completed all work up to the point where the specified signal was added.
        /// </summary>
        /// <param name="fenceValue">The value to wait for. This value should have been returned by a previous call to <see cref="AddSignal"/>.</param>
        public void WaitForSignal(ulong fenceValue) {
            WaitForFence(mFence, fenceValue);
        }

        /// <summary>
        /// Executes a collection of compiled command lists on the GPU and returns the fence value for synchronization.
        /// </summary>
        /// <param name="commandLists">The collection of compiled command lists to execute.</param>
        /// <returns>The fence value for synchronization.</returns>
        ulong ExecuteCommandListsInternal(IEnumerable<CompiledCommandList> commandLists) {
            ID3D12CommandList[] nativeCommandLists = commandLists.Select(c => c.NativeCommandList).ToArray();
            mCommandQueue.ExecuteCommandLists(nativeCommandLists);

            return AddSignal();
        }

        /// <summary>
        /// Checks if a fence has completed its execution on the GPU.
        /// </summary>
        /// <param name="fence">The fence to check.</param>
        /// <param name="fenceValue">The fence value to check for completion.</param>
        /// <returns>True if the fence has completed execution, false otherwise.</returns>
        static bool IsFenceComplete(ID3D12Fence fence, ulong fenceValue)
            => fence.CompletedValue >= fenceValue;

        /// <summary>
        /// Waits until the specified fence has completed its execution on the GPU.
        /// </summary>
        /// <param name="fence">The fence to wait for.</param>
        /// <param name="fenceValue">The fence value to wait for completion.</param>
        void WaitForFence(ID3D12Fence fence, ulong fenceValue) {
            if (IsFenceComplete(fence, fenceValue))
                return;

            mLogger.Write(Serilog.Events.LogEventLevel.Debug, "{MethodName}, Waiting for {requiredFenceValue}, currently at {currentFenceValue}",
                          nameof(WaitForFence), fenceValue, fence.CompletedValue);
            mFenceEvent.Reset();  //Is this required? D3D12Mutiny used it although the project never worked, D3D12Bundles did not, DX12GE constructed a new one each time.
            fence.SetEventOnCompletion(fenceValue, mFenceEvent).CheckError();

            mFenceEvent.WaitOne();
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
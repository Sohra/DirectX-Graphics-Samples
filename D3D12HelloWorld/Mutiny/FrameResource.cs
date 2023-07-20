using Serilog;
using System;
using Vortice.Direct3D12;
using wired.Games;
using wired.Graphics;

namespace D3D12HelloWorld.Mutiny {
    internal class FrameResource : IDisposable {
        bool mIsDisposed;
        ulong mFenceValue;

        public FrameResource(GraphicsDevice device, string? name = null) {
            // The command allocator is used by the main sample class when 
            // resetting the command list in the main update loop. Each frame 
            // resource needs a command allocator because command allocators 
            // cannot be reused until the GPU is done executing the commands 
            // associated with it.
            CommandAllocator = device.NativeDevice.CreateCommandAllocator(CommandListType.Direct);
            if (!string.IsNullOrWhiteSpace(name)) {
                CommandAllocator.Name = $"{name} Direct Allocator ({CommandAllocator.GetHashCode()})";
            }
        }

        public ID3D12CommandAllocator CommandAllocator { get; private set; }

        /// <summary>
        /// Schedules a signal on the GPU command queue. This method can be used in combination with <see cref="WaitForSignal(CommandQueue)"/> to wait for the GPU to complete work up to this point.
        /// </summary>
        /// <param name="commandQueue"></param>
        public void AddSignal(CommandQueue commandQueue) {
            // Signal and increment the fence value.
            mFenceValue = commandQueue.AddSignal();
        }

        public IDisposable CreateProfilingEvent(CommandQueue commandQueue, string eventName, int frameIndex, ILogger mLogger) {
            return new ProfilingEvent(commandQueue.NativeCommandQueue, $"{eventName} Frame index: {frameIndex} with fence value {mFenceValue}", mLogger);
        }

        /// <summary>
        /// Make sure that this frame resource isn't still in use by the GPU.
        /// If it is, wait for it to complete, because resources still scheduled for GPU execution
        /// cannot be modified or else undefined behavior will result.
        /// </summary>
        /// <param name="commandQueue"></param>
        public void WaitForSignal(CommandQueue commandQueue) {
            if (mFenceValue != 0) {
                commandQueue.WaitForSignal(mFenceValue);
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!mIsDisposed) {
                if (disposing) {
                    // dispose managed state (managed objects)
                }

                // set large fields to null
                mIsDisposed = true;
            }
        }
    }
}
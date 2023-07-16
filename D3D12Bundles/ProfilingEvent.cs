using System.Diagnostics;
using Vortice.Direct3D12;

namespace D3D12Bundles {
    /// <summary>
    /// Inspired by PIX Performance Tuning and Debugging, namely, PIXBeginEvent and PIXEndEvent
    /// </summary>
    class ProfilingEvent : IDisposable {
        readonly string mEventName;
        readonly Stopwatch mStopwatch;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context">Context for the event, accepts <see cref="ID3D12GraphicsCommandList"/> or <see cref="ID3D12CommandQueue"/>.</param>
        /// <param name="eventName">The name to use to describe the event</param>
        public ProfilingEvent(object context, string eventName) {
            if (!new[] { typeof(ID3D12GraphicsCommandList), typeof(ID3D12CommandQueue), }.Contains(context.GetType())) {
                throw new ArgumentException($"{context.GetType().Name} is not a valid context for profiling.", nameof(context));
            }
            mEventName = eventName;

            mStopwatch = new Stopwatch();
            mStopwatch.Start();
            Debug.WriteLine($"Starting event: {mEventName}");
        }

        public void Dispose() {
            mStopwatch.Stop();
            Debug.WriteLine($"Ending event: {mEventName}");
            Debug.WriteLine($"Elapsed time: {mStopwatch.ElapsedMilliseconds} ms");
        }
    }
}
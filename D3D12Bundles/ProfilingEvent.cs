using Serilog;
using System.Diagnostics;
using Vortice.Direct3D12;

namespace D3D12Bundles {
    /// <summary>
    /// Inspired by PIX Performance Tuning and Debugging, namely, PIXBeginEvent and PIXEndEvent
    /// </summary>
    class ProfilingEvent : IDisposable {
        readonly string mEventName;
        readonly ILogger mLogger;
        readonly Stopwatch mStopwatch;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context">Context for the event, accepts <see cref="ID3D12GraphicsCommandList"/> or <see cref="ID3D12CommandQueue"/>.</param>
        /// <param name="eventName">The name to use to describe the event</param>
        /// <param name="logger"></param>
        public ProfilingEvent(object context, string eventName, ILogger logger) {
            if (!new[] { typeof(ID3D12GraphicsCommandList), typeof(ID3D12CommandQueue), typeof(wired.Graphics.CommandList), }.Contains(context.GetType())) {
                throw new ArgumentException($"{context.GetType().Name} is not a valid context for profiling.", nameof(context));
            }
            mEventName = eventName;
            mLogger = logger ?? throw new ArgumentNullException(nameof(logger));

            mStopwatch = new Stopwatch();
            mStopwatch.Start();
            mLogger.Verbose($"Starting event: {mEventName}");
        }

        public void Dispose() {
            mStopwatch.Stop();
            mLogger.Verbose($"Ending event: {mEventName}");
            mLogger.Verbose($"Elapsed time: {mStopwatch.ElapsedMilliseconds} ms");
        }
    }
}
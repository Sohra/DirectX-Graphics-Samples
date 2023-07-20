using System;
using System.Diagnostics;

namespace wired.Games {
    /// <summary>
    /// Helper class for animation and simulation timing.
    /// </summary>
    internal class StepTimer {
        // Source timing data uses QPC units.
        readonly long mQpcMaxDelta;
        long mQpcLastTime;

        // Derived timing data uses a canonical tick format.
        TimeSpan mElapsed;
        TimeSpan mTotal;
        TimeSpan mLeftOver;

        // Members for tracking the framerate.
        uint mFrameCount;
        uint mFramesPerSecond;
        uint mFramesThisSecond;
        long mQpcSecondCounter;

        // Members for configuring fixed timestep mode.
        bool mIsFixedTimeStep;
        TimeSpan mTargetElapsed;

        /// <summary>
        /// Get elapsed time since the previous Update call.
        /// </summary>
        public long ElapsedTicks => mElapsed.Ticks;
        /// <summary>
        /// Get elapsed time since the previous Update call.
        /// </summary>
        public double ElapsedSeconds => mElapsed.TotalSeconds;

        /// <summary>
        /// Get total time since the start of the program.
        /// </summary>
        public long TotalTicks => mTotal.Ticks;
        /// <summary>
        /// Get total time since the previous Update call.
        /// </summary>
        public double TotalSeconds => mTotal.TotalSeconds;
        /// <summary>
        /// Get total number of updates since start of the program.
        /// </summary>
        public uint FrameCount => mFrameCount;
        /// <summary>
        /// Get the current framerate.
        /// </summary>
        public uint FramesPerSecond => mFramesPerSecond;

        /// <summary>
        ///
        /// </summary>
        public StepTimer() {
            //For fixed timestep mode, default target to update the frame at a rate of 60fps.
            mTargetElapsed = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

            mQpcLastTime = Stopwatch.GetTimestamp();

            mQpcMaxDelta = Stopwatch.Frequency / 10;
        }

        /// <summary>
        /// Set whether to use fixed or variable timestep mode.
        /// </summary>
        /// <param name="isFixedTimestep"></param>
        public void SetFixedTimeStep(bool isFixedTimestep) {
            mIsFixedTimeStep = isFixedTimestep;
        }

        /// <summary>
        /// Set how often to call Update when in fixed timestep mode.
        /// </summary>
        /// <param name="targetElapsed"></param>
        public void SetTargetElapsedTicks(TimeSpan targetElapsed) {
            mTargetElapsed = targetElapsed;
        }

        /// <summary>
        /// After an intentional timing discontinuity (for instance a blocking IO operation)
        /// call this to avoid having the fixed timestep logic attempt a set of catch-up 
        /// Update calls.
        /// </summary>
        public void ResetElapsedTime() {
            mQpcLastTime = Stopwatch.GetTimestamp();

            //mElapsed = TimeSpan.Zero;
            //mTotal = TimeSpan.Zero;

            mLeftOver = TimeSpan.Zero;
            mFramesPerSecond = 0;
            mFramesThisSecond = 0;
            mQpcSecondCounter = 0;
        }

        /// <summary>
        /// Update timer state, calling the specified Update function the appropriate number of times.
        /// </summary>
        /// <param name="update"></param>
        public void Tick(Action? update = null) {
            // Query the current time.
            long currentTime = Stopwatch.GetTimestamp();
            long timeDelta = currentTime - mQpcLastTime;

            mQpcLastTime = currentTime;
            mQpcSecondCounter += timeDelta;

            // Clamp excessively large time deltas (e.g., after pausing in the debugger).
            if (timeDelta > mQpcMaxDelta) {
                timeDelta = mQpcMaxDelta;
            }

            var lastFrameCount = mFrameCount;

            if (mIsFixedTimeStep) {
                // Fixed timestep update logic

                // If the app is running very close to the target elapsed time (within 1/4 of a millisecond) just clamp
                // the clock to exactly match the target value. This prevents tiny and irrelevant errors
                // from accumulating over time. Without this clamping, a game that requested a 60 fps
                // fixed update, running with vsync enabled on a 59.94 NTSC display, would eventually
                // accumulate enough tiny errors that it would drop a frame. It is better to just round 
                // small deviations down to zero to leave things running smoothly.

                if (Math.Abs(timeDelta - mTargetElapsed.Ticks) < TimeSpan.TicksPerSecond / 4000) {
                    timeDelta = mTargetElapsed.Ticks;
                }

                mLeftOver += TimeSpan.FromTicks(timeDelta);

                while (mLeftOver >= mTargetElapsed) {
                    mElapsed = mTargetElapsed;
                    mTotal += mTargetElapsed;
                    mLeftOver -= mTargetElapsed;
                    mFrameCount++;

                    update?.Invoke();
                }
            }
            else {
                // Variable timestep update logic.
                mElapsed = TimeSpan.FromTicks(timeDelta);
                mTotal += mElapsed;
                mLeftOver = TimeSpan.Zero;
                mFrameCount++;

                update?.Invoke();
            }

            // Track the current framerate.
            if (mFrameCount != lastFrameCount) {
                mFramesThisSecond++;
            }

            if (mQpcSecondCounter >= Stopwatch.Frequency) {
                mFramesPerSecond = mFramesThisSecond;
                mFramesThisSecond = 0;
                mQpcSecondCounter %= Stopwatch.Frequency;
            }
        }
    }
}
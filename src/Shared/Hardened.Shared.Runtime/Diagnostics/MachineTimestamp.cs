using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Diagnostics
{
    /// <summary>
    /// Timestamp that uses the machine ticks, it is only valid on the local machine.
    /// </summary>
    public struct MachineTimestamp
    {
        public static readonly double SecondsToTicksRatio = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
        public static readonly double MillisecondsToTicksRatio = 1 / (double)TimeSpan.TicksPerMillisecond;
        private readonly long _timestamp;

        private MachineTimestamp(long timestamp)
        {
            _timestamp = timestamp;
        }

        /// <summary>
        /// Create timestamp from machine ticks
        /// </summary>
        /// <param name="ticks"></param>
        /// <returns></returns>
        public static MachineTimestamp FromTicks(long ticks)
        {
            return new MachineTimestamp(ticks);
        }

        /// <summary>
        /// Get timestamp for now
        /// </summary>
        public static MachineTimestamp Now => FromTicks(Stopwatch.GetTimestamp());

        /// <summary>
        /// Get the elapsed milliseconds from now to the timestamp
        /// </summary>
        /// <returns></returns>
        public double GetElapsedMilliseconds()
        {
            if (_timestamp == 0)
            {
                throw new Exception("MachineTimestamp was not initialized, can't be used here");
            }


            var currentTime = Stopwatch.GetTimestamp();
            var totalElapsedTime = currentTime - _timestamp;
            return (totalElapsedTime * SecondsToTicksRatio) * MillisecondsToTicksRatio;
        }

        /// <summary>
        /// Get elapsed time from when timestamp was created to now
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public TimeSpan GetElapsedTime()
        {
            if (_timestamp == 0)
            {
                throw new Exception("MachineTimestamp was not initialized, can't be used here");
            }

            var currentTime = Stopwatch.GetTimestamp();
            var totalElapsedTime = currentTime - _timestamp;
            return new TimeSpan((long)(totalElapsedTime * SecondsToTicksRatio));
        }
    }
}

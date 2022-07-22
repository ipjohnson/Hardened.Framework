using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Diagnostics
{
    public struct MachineTimestamp
    {
        public static readonly double SecondsToTicksRatio = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
        public static readonly double MillisecondsToTicksRatio = 1 / (double)TimeSpan.TicksPerMillisecond;
        private readonly long _timestamp;

        private MachineTimestamp(long timestamp)
        {
            _timestamp = timestamp;
        }

        public static MachineTimestamp FromTicks(long ticks)
        {
            return new MachineTimestamp(ticks);
        }

        public static MachineTimestamp Now => FromTicks(Stopwatch.GetTimestamp());

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Utilities;

public enum DateTimePrecision : long
{
    Millisecond = TimeSpan.TicksPerMillisecond,
    Second = TimeSpan.TicksPerSecond,
    Minute = TimeSpan.TicksPerMinute,
    Hour = TimeSpan.TicksPerHour,
    Day = TimeSpan.TicksPerDay,
}

public static class DateTimeExtensions
{
    /// <summary>
    /// Floor date time to a specific precision
    /// </summary>
    /// <param name="dateTime"></param>
    /// <param name="precision"></param>
    /// <returns></returns>
    public static DateTime Floor(this DateTime dateTime, DateTimePrecision precision)
    {
        return new DateTime(dateTime.Ticks - (dateTime.Ticks % (long)precision), dateTime.Kind);
    }
}

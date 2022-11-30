using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Amz.CloudWatch.Dashboards.Models;

public record DashBoardTimeRange(string Mode, long Start, long End, long RelativeStart, DashBoardTimeRange.ZoomRange Zoom)
{
    public record ZoomRange(long Start, long End);
}

public record DashboardTimeZone(string Label, string OffSet, int OffSetInMinutes);

public record DashboardWidgetContext(
    string DashboardName,
    string WidgetId,
    string AccountId,
    string Locale,
    int Period,
    bool IsAutoPeriod,
    string Theme,
    bool LinkCharts,
    int Width,
    int Height,
    DashboardTimeZone TimeZone,
    Dictionary<string, Dictionary<string, string>> Forms,
    Dictionary<string,string> Params)
{
    
    /// <summary>
    /// Get a value from all forms dictionary
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="context"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public T? GetFormValue<T>(string name)
    {
        if (Forms.TryGetValue("all", out var allValues))
        {
            if (allValues.TryGetValue(name, out var value))
            {
                if (value is T tValue)
                {
                    return tValue;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
        }

        return default;
    }
}

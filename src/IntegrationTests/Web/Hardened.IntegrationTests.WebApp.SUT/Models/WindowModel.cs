namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>A body carrying a date-time, which has to state its offset.</summary>
public class WindowModel
{
    public DateTimeOffset EndsAt { get; set; }
}

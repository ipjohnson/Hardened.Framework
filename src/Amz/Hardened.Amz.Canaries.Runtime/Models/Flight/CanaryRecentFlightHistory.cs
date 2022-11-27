namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public record CanaryRecentFlightHistory(
    IReadOnlyList<CanaryFlightInfo> RecentFlights)
{
    public long VersionId { get; set; }
}
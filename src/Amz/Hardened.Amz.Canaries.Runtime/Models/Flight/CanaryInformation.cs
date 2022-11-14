namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public record CanaryInformation(
    string TestClassName,
    string TestMethod,
    DateTime? LastExecuted,
    IReadOnlyList<CanaryFlightInfo> RecentExecutions)
{
    
}
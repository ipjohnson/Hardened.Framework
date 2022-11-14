namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public enum FlightStatus 
{ 
    Scheduled, 
    Inflight, 
    Passed, 
    Canceled,
    Failed
}

public record CanaryFlightInfo(
    string FlightNumber,
    FlightStatus FlightStatus,
    DateTime? FlightTakeOff,
    DateTime? FlightLanded,
    TimeSpan? EstimatedFlightTime,
    bool? Passed
    )
{
    
}
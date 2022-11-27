namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public enum FlightStatus 
{ 
    Inflight, 
    Passed, 
    Canceled,
    Failed
}

public record CanaryFlightInfo(
    string FlightNumber,
    FlightStatus FlightStatus,
    DateTime FlightTakeOff,
    TimeSpan? FlightTime,
    TimeSpan? EstimatedFlightTime
    )
{
    
}
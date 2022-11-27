namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public enum CanaryFlightStyle
{
    /// <summary>
    /// Canary will fail if it runs over frequency time
    /// </summary>
    Strict,
    
    /// <summary>
    /// Canary will only fail when past the timeout (defaults to frequency if not supplied)
    /// </summary>
    Loose
}

public enum CanaryFrequencyUnit
{
    Second,
    Minute,
    Hour,
    Day,
}

/// <summary>
/// Represents the frequency a canary should take flight
/// Frequency 30, Unit Second = execute once every 30 seconds
/// Note the unit can only be as granular as the rule triggering the lambda
/// </summary>
public record CanaryFrequency(
    int Frequency,
    CanaryFrequencyUnit Unit,
    CanaryFlightStyle FlightStyle,
    bool AllowConcurrentExecution)
{
    public TimeSpan Duration
    {
        get
        {
            switch (Unit)
            {
                case CanaryFrequencyUnit.Second:
                    return new TimeSpan(0, 0, Frequency);
             
                case CanaryFrequencyUnit.Minute:
                    return new TimeSpan(0, Frequency, 0);
                
                case CanaryFrequencyUnit.Hour:
                    return new TimeSpan(Frequency, 0, 0);
                
                case CanaryFrequencyUnit.Day:
                    return new TimeSpan(Frequency, 0, 0, 0);
                
                default:
                    throw new NotSupportedException($"Unknown duration type {Unit}");
            }
        }
    }
}
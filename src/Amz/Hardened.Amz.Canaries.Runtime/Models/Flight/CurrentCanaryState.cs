namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public record CurrentCanaryState(
    Dictionary<string, CanaryInformation> Canaries)
{
    
}
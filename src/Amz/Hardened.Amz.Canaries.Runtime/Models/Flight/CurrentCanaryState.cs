namespace Hardened.Amz.Canaries.Runtime.Models.Flight;

public record CurrentCanaryState(
    long VersionId,
    IReadOnlyList<string> DisabledList,
    Dictionary<string, CanaryDefinition> Canaries);
namespace Hardened.Amz.Shared.Lambda.Runtime.Configuration;

public record StageDefinition(string AccountId, string Region, StageType Stage);
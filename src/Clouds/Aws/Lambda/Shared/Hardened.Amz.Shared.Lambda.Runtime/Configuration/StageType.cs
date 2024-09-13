namespace Hardened.Amz.Shared.Lambda.Runtime.Configuration;

public interface IStageType {
    string StageName { get; }
    
    bool IsProduction { get; }
}


public record StageType(string StageName, bool IsProduction = false) : IStageType {
    
    public static StageType Dev = new(nameof(Dev).ToLower());
    
    public static StageType Beta = new(nameof(Beta).ToLower());
    
    public static StageType Gamma = new(nameof(Gamma).ToLower());
    
    public static StageType Production => new(nameof(Production).ToLower(), true);
}
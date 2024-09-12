namespace Hardened.Amz.Shared.Lambda.Runtime.Configuration;

public interface IStageType {
    string StageName { get; }
    
    bool IsProduction { get; }
}


public record StageType(string StageName, bool IsProduction = false) : IStageType {
    
    public static StageType Dev = new(nameof(Dev));
    
    public static StageType Beta = new(nameof(Beta));
    
    public static StageType Gamma = new(nameof(Gamma));
    
    public static StageType Prod => new(nameof(Prod), true);
}
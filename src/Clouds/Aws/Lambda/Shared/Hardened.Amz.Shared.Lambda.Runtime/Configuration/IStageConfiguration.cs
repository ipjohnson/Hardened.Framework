namespace Hardened.Amz.Shared.Lambda.Runtime.Configuration;

public interface ISupportedRegion {
    string Name { get; }
}

public interface IStageConfiguration<out TRegion, out TStageType> 
    where TRegion : ISupportedRegion where TStageType : IStageType {
    
    TRegion Region { get; }
    
    TStageType Stage { get; }
}

public class StageConfiguration(KnownRegion region, StageType stage) 
    : IStageConfiguration<KnownRegion, StageType> {

    public KnownRegion Region {
        get;
    } = region;

    public StageType Stage {
        get;
    } = stage;
}

public class KnownRegion(string name) : ISupportedRegion {
    public string Name {
        get;
    } = name;
    
    
    public static KnownRegion UsEast1 => new KnownRegion("us-east-1");
    public static KnownRegion UsEast2 => new KnownRegion("us-east-2");
    public static KnownRegion UsWest1 => new KnownRegion("us-west-1");
    public static KnownRegion UsWest2 => new KnownRegion("us-west-2");
}
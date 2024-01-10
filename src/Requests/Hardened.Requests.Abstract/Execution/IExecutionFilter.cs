namespace Hardened.Requests.Abstract.Execution;

public enum ExecutionFilterOrder {
    Init = -10000,
    
    FullRequestMetrics = -7000,
    
    RetryFilter = -5000,
    
    BeforeSerialize = -1,

    BindParameters = 0,

    First = 1,

    Second = 2,

    Third = 3,

    Normal = 100,

    Last = int.MaxValue,
}

public interface IExecutionFilter {
    Task Execute(IExecutionChain chain);
}
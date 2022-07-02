namespace Hardened.Requests.Abstract.Execution
{
    public enum ExecutionFilterOrder
    {
        BeforeSerialize = -1,

        BindParameters = 0,

        First = 1,

        Second = 2,

        Third = 3,

        Normal = 100,
        
        Last = Int32.MaxValue, 
    }
    
    public interface IExecutionFilter
    {
        Task Execute(IExecutionChain chain);
    }
}

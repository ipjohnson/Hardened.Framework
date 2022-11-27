namespace Hardened.Shared.Testing;

public interface IRetryEngine
{
    /// <summary>
    /// Delay in milliseconds, default is 1 second
    /// </summary>
    int Delay { get; set; }
    
    Task TillTrue(
        Func<Task<bool>> testFunc,
        string description, params object[] parameters);

    Task TillFalse(Func<Task<bool>> testFunc,
        string description, params object[] parameters);

    Task<T> TillValue<T>(Func<Task<T>> value, 
        string description, params object[] parameters);
}
namespace Hardened.Requests.Abstract.Errors;

/// <summary>
/// The status an exception from outside the framework stands for, such as a host refusing a
/// request it could not read.
/// </summary>
/// <remarks>
/// Kestrel refuses a body over its size limit by throwing its own <c>BadHttpRequestException</c>
/// from the read, inside the pipeline, where nothing knew the type, so the request answered 500. A
/// host registers one of these to say what its exceptions mean, and the exception converter asks
/// every registered reader before it decides a failure is a fault.
/// </remarks>
public interface IExceptionStatusReader
{
    /// <summary>
    /// The status <paramref name="exception"/> stands for, or null when this reader does not know
    /// it.
    /// </summary>
    int? StatusOf(Exception exception);
}

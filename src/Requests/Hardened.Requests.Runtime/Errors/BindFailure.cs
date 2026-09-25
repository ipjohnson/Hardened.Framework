namespace Hardened.Requests.Runtime.Errors;

/// <summary>
/// What a failure to bind a request's parameters is recorded as.
/// </summary>
/// <remarks>
/// A <see cref="FormatException"/> thrown while binding is about the request: a custom JSON
/// converter or binding attribute found a value it could not read, and the caller has to hear
/// which. Thrown from a handler, the same type is a server fault, and
/// <see cref="ExceptionToModelConverter"/> answers it 500 without its message. So the binding
/// filters make it a 400 here, keeping its message, with the original as the inner exception.
/// </remarks>
internal static class BindFailure
{
    public static Exception For(Exception exception) =>
        exception is FormatException
            ? new BadRequestException(exception.Message, exception)
            : exception;
}

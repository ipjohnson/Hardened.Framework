namespace Hardened.CloudEvents;

/// <summary>
/// A request that claimed to be a CloudEvent and was not one.
/// </summary>
/// <remarks>
/// Raised only after a reader has been told which form to expect: a structured body that is not a
/// JSON object, or either form missing one of the four required context attributes. A request
/// that is merely not a CloudEvent is answered by <see cref="CloudEventReader.IsStructured"/> and
/// <see cref="CloudEventReader.IsBinary"/> returning false, which costs no exception.
/// </remarks>
public sealed class CloudEventFormatException : Exception {
    public CloudEventFormatException(string message) : base(message) {
    }

    public CloudEventFormatException(string message, Exception inner) : base(message, inner) {
    }
}

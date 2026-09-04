namespace Hardened.Requests.Abstract.Compression;

/// <summary>
/// The content coding an operation prefers when the client accepts more than one.
/// </summary>
/// <remarks>
/// An enum with a zero member rather than a nullable, because an attribute property cannot be a
/// nullable enum. <see cref="Default"/> means the operation expresses no preference and the
/// configured order decides.
/// </remarks>
public enum CompressionType {
    /// <summary>Follow the configured preference order.</summary>
    Default = 0,

    /// <summary>Try gzip first.</summary>
    GZip,

    /// <summary>Try Brotli first.</summary>
    Br
}

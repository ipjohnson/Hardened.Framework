namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// What a service answers when a client asks for a media type the operation does not produce.
/// </summary>
public enum ContentNegotiationMode {
    /// <summary>
    /// Answer <c>406 Not Acceptable</c>. The default.
    /// </summary>
    /// <remarks>
    /// A client that names media types and shares none with the operation has asked for something
    /// that does not exist, and 406 is the answer HTTP defines for that. Nothing about it is the
    /// API author's to describe - unlike a 404, which is a domain outcome an operation may or may
    /// not report - so it is not derived from the document and does not need declaring.
    /// </remarks>
    Strict = 0,

    /// <summary>
    /// Serialize with the default serializer anyway, which is what the framework did before this
    /// existed.
    /// </summary>
    /// <remarks>
    /// For an application that would rather answer something than nothing - or one migrating, whose
    /// clients send <c>Accept: application/json</c> at operations that never produced JSON and were
    /// answered with the declared string wrapped in quotes.
    /// </remarks>
    Lenient = 1
}

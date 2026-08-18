namespace Hardened.Web.StaticContent;

/// <summary>
/// Where the files a mount serves come from, and what is known about them.
/// </summary>
/// <remarks>
/// <para>
/// Split in two because the pipeline needs the answers at two different moments and only one of
/// them may block. <see cref="Locate"/> runs during routing, where
/// <c>IWebExecutionRequestHandlerProvider.GetExecutionRequestHandler</c> is synchronous and the
/// only question is whether this mount answers at all - which decides between a handler, a 405 and
/// declining so something else can answer. <see cref="Load"/> runs inside the handler's chain,
/// after authorization, where reading a file is allowed to take as long as it takes.
/// </para>
/// <para>
/// <b>An interface because the answer comes from somewhere different in each environment.</b> A
/// production build knows its content at compile time and should look it up; a developer iterating
/// on a page needs the file re-read when it changes. Those are two implementations of this, not two
/// pipelines - everything downstream, from content negotiation to the shape of a 304, is written
/// once against this.
/// </para>
/// </remarks>
public interface IStaticContentSource {

    /// <summary>
    /// Whether this source has anything to serve at all.
    /// </summary>
    /// <remarks>
    /// False for a mount whose directory is not there, which is the ordinary case for an
    /// application that serves no static content. It is asked once per request ahead of any path
    /// work, so that application pays a field read.
    /// </remarks>
    bool Enabled { get; }

    /// <summary>
    /// What would answer <paramref name="requestPath"/>, or null if nothing here does.
    /// </summary>
    /// <remarks>
    /// Synchronous, and deliberately: it runs while routing is still deciding who answers. It may
    /// touch the filesystem to ask whether a file exists - that is a stat, not a read - and must not
    /// open one.
    /// </remarks>
    StaticContentLocation? Locate(string requestPath);

    /// <summary>
    /// The entry for <paramref name="location"/>, reading it if this is the first time.
    /// </summary>
    /// <returns>
    /// Null if the file has gone since <see cref="Locate"/> saw it. Rare, and a race rather than a
    /// mistake: something deleted the file between routing and serving.
    /// </returns>
    ValueTask<StaticContentEntry?> Load(StaticContentLocation location);
}

/// <summary>
/// A file this mount will serve, identified but not yet read.
/// </summary>
/// <param name="Key">
/// What the entry is cached under. The resolved path of the resource, which for a pre-compressed
/// sibling is the name without the suffix - <c>app.js</c> for <c>app.js.gz</c> - so both spellings
/// of one resource share an entry.
/// </param>
/// <param name="FilePath">The file to read, suffix included.</param>
/// <param name="ContentEncoding">
/// The coding <paramref name="FilePath"/> is already in, or null when it is the resource itself.
/// </param>
/// <param name="Cached">The entry, when it has been read before.</param>
/// <param name="IsFallback">
/// Whether this was reached through the fall back file rather than by existing at the path asked
/// for. It decides what a non-GET does: a verb a real file does not answer is a 405, and the same
/// verb on a path that only exists because a single-page application catches everything is a 404.
/// Answering 405 there would tell a client that <c>POST /api/typo</c> reached a resource.
/// </param>
public readonly record struct StaticContentLocation(
    string Key,
    string FilePath,
    string? ContentEncoding,
    StaticContentEntry? Cached,
    bool IsFallback);

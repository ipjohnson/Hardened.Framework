namespace Hardened.Web.StaticContent;

/// <summary>
/// What the build found in the content directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>The point of computing this at build time is that the runtime stops discovering things.</b>
/// Every expensive or dangerous behaviour the file system source has exists because it is finding
/// something out per request: whether a file exists, what its hash is, whether it is worth
/// compressing, whether a link escapes the root. The build already knows all of it, so a request
/// becomes a dictionary lookup and a write - no hashing, no compressing, no unbounded cache, and
/// nothing that two concurrent first-requests can both do.
/// </para>
/// <para>
/// Implemented by a class the build task emits into the application's own assembly, which is the
/// only place the bytes and the registration can both live. An application with no manifest falls
/// back to reading the directory, so the task is an optimisation and a verification layer rather
/// than a requirement.
/// </para>
/// </remarks>
public interface IStaticContentManifest {

    /// <summary>Every file, keyed by the path a request asks for.</summary>
    IReadOnlyList<StaticContentManifestEntry> Entries { get; }

    /// <summary>
    /// The route the fall back file answers at, or null when the application configured none.
    /// </summary>
    /// <remarks>
    /// Resolved at build time against what is actually present, so a name that does not exist is a
    /// build error rather than an exception on the first request that reaches it.
    /// </remarks>
    string? FallBackRoute { get; }
}

/// <summary>
/// One file, as the build recorded it.
/// </summary>
/// <param name="RoutePath">The path a request asks for, with a leading slash.</param>
/// <param name="Hash">
/// A content hash, base64. SHA-256 rather than MD5: the value is opaque and any hash would do for
/// a validator, but MD5 throws outright on a FIPS-enforcing host - which would take the whole
/// static path down on the first request rather than degrade.
/// </param>
/// <param name="LastModifiedUtcTicks">
/// The file's write time, as ticks, because a <c>DateTimeOffset</c> is not a constant and this has
/// to survive being written into generated source.
/// </param>
/// <param name="Content">
/// The bytes, embedded, or null when the file is served from disk instead. Embedded content costs
/// assembly size and buys a deployment that cannot go stale and needs no file access at all - the
/// right trade for a shell and the wrong one for a video.
/// </param>
/// <param name="GZipContent">
/// The gzip-compressed bytes, embedded, or null when compressing did not make the file smaller or
/// the file is not embedded. Compressed once, at build, at the level that produces the smallest
/// result - the cost is paid by whoever runs the build rather than by the first request.
/// </param>
/// <param name="RelativePath">
/// Where to read the file, relative to the content root, when it is not embedded.
/// </param>
public sealed record StaticContentManifestEntry(
    string RoutePath,
    string Hash,
    long Length,
    long LastModifiedUtcTicks,
    byte[]? Content,
    byte[]? GZipContent,
    string? RelativePath) {

    /// <summary>Whether the bytes travel in the assembly rather than beside it.</summary>
    public bool IsEmbedded => Content != null;

    public DateTimeOffset LastModified =>
        new(LastModifiedUtcTicks, TimeSpan.Zero);
}

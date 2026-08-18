using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.CacheControl;

namespace Hardened.Web.StaticContent;

public interface IStaticContentConfiguration {
    string Path { get; }

    CacheControlEnum CacheControlType { get; }

    int? CacheMaxAge { get; }

    bool Immutable { get; }

    bool EnableETag { get; }

    string? FallBackFile { get; }

    bool CompressTextContent { get; }

    Action<IExecutionContext>? OnPrepareResponse { get; }

    /// <summary>
    /// What this mount requires of its caller, or null to inherit the application's posture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null is not "public". It means the mount carries no requirement of its own, which under
    /// <c>[RequireAuthorization]</c> is denied and everywhere else is public - the same answer an
    /// unannotated handler gets, because it reaches the same code. A convention can narrow it in
    /// either case.
    /// </para>
    /// <para>
    /// Stated as a <see cref="Authorization.Requirement"/> rather than through an attribute because
    /// <c>IExecutionRequestHandlerInfo.Requirement</c> is first-class data, and documents this as
    /// the supported route for a handler registered by hand: "a handler registered by hand can state
    /// one without inventing an attribute to carry it".
    /// </para>
    /// <example>
    /// <code>
    /// configuration.Requirement = Requirement.Grant("docs:read");
    /// </code>
    /// </example>
    /// </remarks>
    Requirement? Requirement { get; }

    /// <summary>
    /// Whether a file, once read, is kept - or re-read on every request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off is how a developer iterates: edit a file, reload, see it. There is no watcher, no change
    /// token and no invalidation, because not caching needs none of them - a stat and a read of a
    /// file the operating system already has in its page cache costs tens of microseconds, which is
    /// nothing against a browser round trip.
    /// </para>
    /// <para>
    /// It replaces <c>Debugger.IsAttached</c>, which was sampled once in a singleton constructor and
    /// so answered "was a debugger attached when the container was built" rather than "is this a
    /// development build" - and which disabled only the write half, so every request still paid a
    /// dictionary lookup that could never hit.
    /// </para>
    /// <para>
    /// Compression follows it. Compressing on the way into a cache is paid once and recovered on
    /// every request after; compressing with no cache is paid on every request, at the level that
    /// produces the smallest result, which is the slowest one there is.
    /// </para>
    /// </remarks>
    bool CacheContent { get; }

    /// <summary>
    /// Whether byte ranges are served, and advertised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On, because the clients that need it cannot ask twice: Safari issues a range request before
    /// it will play a media element at all, and a download manager resuming has no other way to
    /// avoid starting over. Off is for a mount serving nothing seekable, where advertising the
    /// capability only invites a request shape that costs a second code path.
    /// </para>
    /// <para>
    /// Only ever applies to a representation served as stored. A byte offset into a gzip stream is
    /// not a byte offset into the resource, and <c>Content-Range</c> has no way to say which one it
    /// meant.
    /// </para>
    /// </remarks>
    bool EnableRangeRequests { get; }

    /// <summary>
    /// Whether a path with a hidden segment - <c>.env</c>, <c>.git</c>, <c>.htpasswd</c> - is
    /// served.
    /// </summary>
    /// <remarks>
    /// Off, because the common case is a build step that copied a directory wholesale and nobody
    /// looked. <c>.well-known</c> is served either way: ACME challenges and <c>security.txt</c> live
    /// under it, and refusing it breaks certificate renewal in a way nobody connects back to a
    /// static content setting.
    /// </remarks>
    bool ServeHiddenFiles { get; }
}

public class StaticContentConfiguration : IStaticContentConfiguration {
    public string Path { get; set; } = "wwwroot";

    public CacheControlEnum CacheControlType { get; set; } = CacheControlEnum.MaxAge | CacheControlEnum.Public;

    public int? CacheMaxAge { get; set; } = 0;

    public bool Immutable { get; set; }

    public bool EnableETag { get; set; } = true;

    public string? FallBackFile { get; set; }

    public bool CompressTextContent { get; set; } = true;

    public Action<IExecutionContext>? OnPrepareResponse { get; set; }

    /// <inheritdoc />
    public Requirement? Requirement { get; set; }

    /// <inheritdoc />
    public bool CacheContent { get; set; } = true;

    /// <inheritdoc />
    public bool EnableRangeRequests { get; set; } = true;

    /// <inheritdoc />
    public bool ServeHiddenFiles { get; set; }
}
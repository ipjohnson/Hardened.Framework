namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// What uniquely names a handler, and therefore what routing keys on.
/// </summary>
/// <remarks>
/// <para>
/// For HTTP that is a verb and a path - <c>GET:/pets/{id}</c>. For an RPC protocol wearing HTTP as
/// an envelope it is an exact token carried in a header: awsJson1_0 sends every operation to
/// <c>POST /</c> and says which one in <c>X-Amz-Target</c>, so the path and verb distinguish nothing
/// and <see cref="DispatchKey"/> distinguishes everything.
/// </para>
/// <para>
/// Both are held rather than one replacing the other, because a request still has to arrive at
/// <c>POST /</c> before anything can dispatch on a header - and because an application may serve
/// both kinds at once, which is what makes this the right place for the distinction. It is already
/// the value the incremental pipeline compares and hashes handlers by; the dispatch key joins that
/// rather than sitting beside it, or two operations differing only in target would compare equal and
/// the generator would serve one handler for both.
/// </para>
/// </remarks>
public class RequestHandlerNameModel {

    public RequestHandlerNameModel(string path, string method)
        : this(path, method, null, null) { }

    public RequestHandlerNameModel(
        string path, string method, string? dispatchHeader, string? dispatchKey) {
        Path = path;
        Method = method;
        DispatchHeader = dispatchHeader;
        DispatchKey = dispatchKey;
    }

    public string Path { get; }

    public string Method { get; }

    /// <summary>The request header carrying <see cref="DispatchKey"/>, or null for path routing.</summary>
    public string? DispatchHeader { get; }

    /// <summary>The exact token this handler answers to, or null for path routing.</summary>
    public string? DispatchKey { get; }

    /// <summary>Whether this handler is selected by an exact token rather than by route.</summary>
    public bool IsDispatched => DispatchKey != null && DispatchHeader != null;

    public override bool Equals(object obj) {
        if (obj is not RequestHandlerNameModel requestHandlerNameModel) {
            return false;
        }

        return Path.Equals(requestHandlerNameModel.Path) &&
               Method.Equals(requestHandlerNameModel.Method) &&
               DispatchHeader == requestHandlerNameModel.DispatchHeader &&
               DispatchKey == requestHandlerNameModel.DispatchKey;
    }

    /// <summary>
    /// The handler's identity as one string.
    /// </summary>
    /// <remarks>
    /// A dispatched handler reads as its token, because that is what names it - <c>PetStore.GetPet</c>
    /// rather than <c>POST:/</c>, which every operation in an awsJson service would share.
    /// </remarks>
    public override string ToString() {
        return DispatchKey ?? Method + ":" + Path;
    }

    public override int GetHashCode() {
        unchecked {
            var hashCode = (Path.GetHashCode() * 397) ^ Method.GetHashCode();

            return (hashCode * 397) ^ (DispatchKey?.GetHashCode() ?? 0);
        }
    }
}

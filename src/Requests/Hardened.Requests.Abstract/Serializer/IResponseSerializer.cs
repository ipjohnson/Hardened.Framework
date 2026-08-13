using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// Where a response serializer sits relative to the others. Lower runs first, matching
/// <c>ExecutionFilterOrder</c>.
/// </summary>
/// <remarks>
/// <para>
/// Order exists because the alternative is registration order, and registration order here is not
/// something an application can control: within a module, DependencyModules sorts by whether a
/// registration is conditional and then by implementation type name, so which serializer won came
/// down to how two classes sorted alphabetically. Renaming one changed which one handled a request.
/// </para>
/// <para>
/// Values are spaced so a serializer can be slotted between two of them without renumbering.
/// </para>
/// </remarks>
public enum ResponseSerializerOrder {
    /// <summary>
    /// Rendered output. Ahead of everything because a response that names a view is asking for that
    /// view specifically, and would otherwise be taken by whichever serializer matched the request's
    /// <c>Accept</c> - which, for a browser, is usually the JSON one.
    /// </summary>
    Template = -1000,

    /// <summary>A serializer for one specific media type, ahead of the general-purpose ones.</summary>
    Specialized = -100,

    /// <summary>The default, and where the JSON serializers sit.</summary>
    Normal = 0
}

public interface IResponseSerializer {
    bool IsDefaultSerializer { get; }

    /// <summary>
    /// Lower is tested first. Defaults to <see cref="ResponseSerializerOrder.Normal"/>, so an
    /// existing serializer that does not care keeps working unchanged.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IsDefaultSerializer"/> on purpose. Order decides who is asked first;
    /// <c>IsDefaultSerializer</c> decides who answers when nobody claims the context at all. A
    /// specialist sitting ahead of JSON must not stop JSON being the fallback for
    /// <c>Accept: */*</c>, which is the most common request shape there is.
    /// </remarks>
    int Order => (int)ResponseSerializerOrder.Normal;

    bool CanProcessContext(IExecutionContext context);

    Task SerializeResponse(IExecutionContext context);
}

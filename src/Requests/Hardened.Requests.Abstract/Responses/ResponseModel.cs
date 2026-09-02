namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// How a module's handlers say they can answer with more than one kind of response.
/// </summary>
/// <remarks>
/// <para>
/// Three values from the start, including one that is not implemented yet. An enum that ships with
/// two and gains a third later invites a binary assumption into the generator internals - a
/// <c>bool useUnion</c>, an <c>if/else</c> - and every one of those has to be found again when the
/// third arrives. Declaring all three now costs a diagnostic and settles the shape.
/// </para>
/// <para>
/// It is a module-wide policy rather than a per-handler one because the alternative is an
/// application where two endpoints declare the same thing two ways, and a reader has to check each
/// handler to know which. A module that wants both splits into two modules, which is a real
/// boundary rather than an annotation.
/// </para>
/// </remarks>
public enum ResponseModel {

    /// <summary>
    /// One return type per handler, other statuses reached by throwing. What an application that
    /// says nothing gets today; new projects scaffold as <see cref="Response"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a legacy mode. An organisation that wants authorization decided only in filters closes
    /// the return path deliberately and leaves the throw path open, which is a coherent policy - so
    /// this keeps working alongside the other two rather than being replaced by them.
    /// </para>
    /// <para>
    /// Named for its mechanism, like the other two: errors are thrown, and <c>[Throws&lt;T&gt;]</c>
    /// is how the thrown statuses reach the document. It was called <c>Standard</c> until 0.19.0 -
    /// a positional name, and the position moved. The old member remains below as an alias so the
    /// rename is a warning rather than a break.
    /// </para>
    /// </remarks>
    Throws,

    /// <summary>
    /// The handler returns <c>Response&lt;T1..Tn&gt;</c>, a Hardened struct that any C# compiler can
    /// build.
    /// </summary>
    /// <remarks>
    /// The answer for a consumer who wants a declared response set without moving to .NET 11. It is
    /// matched structurally rather than by name, which is what lets the same generator code serve
    /// this and <see cref="Union"/> from one path.
    /// </remarks>
    Response,

    /// <summary>
    /// The handler returns a C# 15 language union.
    /// </summary>
    /// <remarks>
    /// Requires a C# 15 compiler and reports a build error below it rather than quietly emitting the
    /// struct instead. A fallback would mean one module setting producing a different public API
    /// depending on who built it, which is the same objection that keeps any Hardened type from
    /// carrying <c>[Union]</c>.
    /// </remarks>
    Union,

    /// <summary>
    /// The old name for <see cref="Throws"/>.
    /// </summary>
    /// <remarks>
    /// Same value, so <c>[ResponseModel(ResponseModel.Standard)]</c> keeps meaning what it meant.
    /// Goes away at 1.0.
    /// </remarks>
    [Obsolete("The Standard response model was renamed Throws in 0.19.0; write ResponseModel.Throws.")]
    Standard = Throws
}

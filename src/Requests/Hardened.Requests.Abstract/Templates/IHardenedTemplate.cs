using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Templates;

/// <summary>
/// A view that renders itself.
/// </summary>
/// <remarks>
/// <para>
/// The engine holds this one and never does generic gymnastics: a model arrives as
/// <see cref="object"/> because the engine cannot know the type, and the cast happens in exactly
/// one place - the base class every template derives from - rather than being emitted per handler
/// or reached for with reflection.
/// </para>
/// <para>
/// The template author never sees this interface. <see cref="IHardenedTemplate{TModel}"/> is what
/// a base class implements publicly, and the untyped <c>Attach</c> is implemented explicitly, so
/// the only <c>Attach</c> visible on a template is the typed one.
/// </para>
/// </remarks>
public interface IHardenedTemplate {
    /// <summary>
    /// Hands the template its model and the request it is rendering for. Called once, before
    /// <see cref="RenderAsync"/>.
    /// </summary>
    void Attach(object? model, IExecutionContext context);

    /// <summary>
    /// What this template produces, which follows from the base class it was built on rather than
    /// from a file extension or a registry.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// The request being rendered, as attached.
    /// </summary>
    /// <remarks>
    /// On the interface rather than left to each base class because generated code has to reach it:
    /// a per-module template base exposes a <c>Links</c> property, and resolving the links type
    /// means reaching the request's services. A base is free to implement this explicitly - and
    /// <c>HardenedHtmlTemplate</c> does - so it stays off the surface a template author sees.
    /// </remarks>
    IExecutionContext Context { get; }

    /// <summary>
    /// Writes the rendered output.
    /// </summary>
    /// <remarks>
    /// A class deriving from RazorBlade's <c>HtmlTemplate</c> satisfies this implicitly - the
    /// inherited member matches, with no adapter. Verified.
    /// </remarks>
    Task RenderAsync(TextWriter writer, CancellationToken cancellationToken = default);
}

/// <summary>
/// A view over a particular model type.
/// </summary>
/// <remarks>
/// The typed half of the pair. Its use is not only ergonomic: a generated assignment
/// <c>IHardenedTemplate&lt;FortunePage&gt; _check = new Views.Fortunes();</c> is the one thing that
/// makes "this template's model matches this handler's return type" a compile error, across a
/// generator boundary where nothing can inspect the other generator's output.
/// </remarks>
/// <typeparam name="TModel">
/// Contravariant, so a template written against a base model serves a handler returning a derived
/// one.
/// </typeparam>
public interface IHardenedTemplate<in TModel> : IHardenedTemplate {
    void Attach(TModel model, IExecutionContext context);
}

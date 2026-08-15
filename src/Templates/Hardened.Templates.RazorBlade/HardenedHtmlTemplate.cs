using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Templates;

namespace Hardened.Templates.RazorBlade;

/// <summary>
/// The base a generated HTML template base derives from: a RazorBlade template that knows its
/// model and the request it is rendering for.
/// </summary>
/// <remarks>
/// <para>
/// <b>It inherits the non-generic RazorBlade base, and that is load-bearing.</b> Both of the
/// obvious alternatives were tried and neither works:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>RazorBlade.HtmlTemplate&lt;TModel&gt;.Model</c> is read-only - assigning it is
/// <c>CS0200</c> - so a model cannot be attached after construction, and attaching after
/// construction is the whole shape of this design.
/// </item>
/// <item>
/// RazorBlade emits the <c>(TModel model) : base(model)</c> constructor <em>only for its own base
/// types</em>. A custom generic base gets parameterless construction from the template and fails
/// with <c>CS7036</c>.
/// </item>
/// </list>
/// <para>
/// Inheriting the non-generic base and declaring our own <see cref="Model"/> sidesteps both:
/// parameterless construction is correct, and there is no constructor for anyone to generate.
/// Verified end to end - <c>@inherits</c>, <c>@Model</c> and a generated links property all render.
/// </para>
/// <para>
/// <c>RenderAsync</c> is not implemented here. RazorBlade's own inherited member satisfies
/// <see cref="IHardenedTemplate.RenderAsync"/> implicitly, with no adapter. Also verified, because
/// it does not look like it should from the declarations alone.
/// </para>
/// </remarks>
public abstract class HardenedHtmlTemplate<TModel> : global::RazorBlade.HtmlTemplate, IHardenedTemplate<TModel> {

    /// <summary>The value the handler returned.</summary>
    public TModel Model { get; private set; } = default!;

    /// <summary>
    /// The request being rendered. Protected rather than public: a view needs it to reach services
    /// - a generated links type resolves from it - but it is plumbing rather than something a
    /// template should be reading request state out of.
    /// </summary>
    protected IExecutionContext Context { get; private set; } = default!;

    /// <inheritdoc />
    public virtual string ContentType => "text/html; charset=utf-8";

    public void Attach(TModel model, IExecutionContext context) {
        Model = model;
        Context = context;
    }

    /// <summary>
    /// Explicit, so the only <c>Attach</c> a template author sees is the typed one.
    /// </summary>
    /// <remarks>
    /// The guard moved here from <c>RazorBladeTemplateDescriptor</c>, which is being deleted, and
    /// it must not die with it. Without it a null or mismatched model surfaces as a bare
    /// <c>InvalidCastException</c> from inside this method, naming no template at all - and the
    /// null case is the likely one, because a handler returning nothing on an error path is
    /// ordinary.
    /// </remarks>
    void IHardenedTemplate.Attach(object? model, IExecutionContext context) {
        if (model is null && default(TModel) is not null) {
            throw new InvalidOperationException(
                $"Template '{GetType().Name}' needs a {typeof(TModel).Name} model but the response " +
                "value was null.");
        }

        if (model is not null and not TModel) {
            throw new InvalidOperationException(
                $"Template '{GetType().Name}' needs a {typeof(TModel).Name} model but the response " +
                $"value was {model.GetType().Name}.");
        }

        Attach((TModel)model!, context);
    }
}

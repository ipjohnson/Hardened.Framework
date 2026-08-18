using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

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
/// Note that "end to end" there means the inheritance shape; the render path itself is covered by
/// <c>SynchronousWriteRegressionTests</c>, which drives a body that refuses synchronous writes.
/// </para>
/// </remarks>
public abstract class HardenedHtmlTemplate<TModel> : global::RazorBlade.HtmlTemplate,
    IHardenedResponseOutput<TModel> {

    /// <summary>
    /// StreamWriter's parameterless UTF8 encoding writes a byte order mark, which lands in the
    /// response body ahead of the markup and is visible in the rendered page.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>The value the handler returned.</summary>
    public TModel Model { get; private set; } = default!;

    /// <summary>
    /// The request being rendered.
    /// </summary>
    /// <remarks>
    /// Protected: a view needs it to reach services - a generated links property resolves from it -
    /// but it is plumbing rather than something a template should be reading request state out of.
    /// It is part of the contract a <c>[TemplateBase]</c> declares rather than of
    /// <see cref="IHardenedResponseOutput"/>, because only a generated template base needs it.
    /// </remarks>
    protected IExecutionContext Context { get; private set; } = default!;

    /// <summary>What this view produces. Overridden by the generated base, from its marker.</summary>
    public virtual string ContentType => "text/html; charset=utf-8";

    /// <inheritdoc />
    public bool SupportsContentType(string? accept, IExecutionContext context) {
        var accepted = AcceptedContentTypes.Parse(accept).MediaTypes;

        for (var i = 0; i < accepted.Count; i++) {
            if (MediaType.Matches(accepted[i], ContentType)) {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Rendered into a buffer, then copied to the body in one asynchronous write.</b> The
    /// obvious implementation - a <c>StreamWriter</c> around <c>Response.Body</c> - cannot work,
    /// because RazorBlade's <c>WriteLiteral</c> is a synchronous <c>TextWriter.Write</c>. A
    /// <c>StreamWriter</c> flushes when its character buffer fills, so a view whose output passed
    /// the default 1 KiB flushed synchronously onto the response stream, and every server that
    /// refuses synchronous IO - Kestrel and the ASP.NET host both, by default - threw
    /// <c>Synchronous operations are disallowed</c> from the middle of the render.
    /// </para>
    /// <para>
    /// The status line had already gone out by then, so the client got <c>200</c> with an empty
    /// body. Views under 1 KiB rendered fine, which is what made it survive: it is a function of
    /// how much a page emits rather than of anything the application does, so it passes every
    /// small test and fails on the first real page.
    /// </para>
    /// <para>
    /// Buffering removes the synchronous write rather than tolerating it - flushes to a
    /// <c>MemoryStream</c> do no IO - and is what ASP.NET Core MVC does for the same reason. The
    /// buffer comes from <see cref="IMemoryStreamPool"/> where the application registered one, so
    /// a page costs a pooled stream rather than a fresh allocation per request.
    /// </para>
    /// <para>
    /// The tests that missed this wrote to a <c>MemoryStream</c>, which accepts synchronous writes.
    /// <c>SynchronousWritesRejectedStream</c> in the test project is the double that does not.
    /// </para>
    /// </remarks>
    public async Task WriteOutput(IExecutionContext context) {
        Attach(context.Response.ResponseValue, context);

        // Only when the handler has not already chosen one. Checked for empty as well as null
        // because the ASP.NET Core host coerces a null assignment to "", so a response that has
        // been touched and left unset reads back as empty rather than null.
        if (string.IsNullOrEmpty(context.Response.ContentType)) {
            context.Response.ContentType = ContentType;
        }

        // GetService rather than GetRequiredService: the pool is registered by the shared runtime
        // module, and a view rendered from a container composed by hand - which the tests do, and
        // an embedding host may - must still render.
        using var reservation = context.RequestServices.GetService<IMemoryStreamPool>()?.Get();

        var buffer = reservation?.Item ?? new MemoryStream(1024);

        // leaveOpen so disposing the writer does not close a stream the pool is about to reclaim.
        await using (var writer = new StreamWriter(buffer, Utf8NoBom, -1, true)) {
            await RenderAsync(writer, context.CancellationToken);
        }

        buffer.Position = 0;

        await buffer.CopyToAsync(context.Response.Body, context.CancellationToken);
    }

    /// <summary>
    /// Takes the model, with the cast written once here rather than emitted per handler.
    /// </summary>
    /// <remarks>
    /// The guard moved here from <c>RazorBladeTemplateDescriptor</c>, which was deleted, and it must
    /// not die with it. Without it a null or mismatched model surfaces as a bare
    /// <c>InvalidCastException</c> from inside this method, naming no template at all - and the null
    /// case is the likely one, because a handler returning nothing on an error path is ordinary.
    /// </remarks>
    private void Attach(object? model, IExecutionContext context) {
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

        Model = (TModel)model!;
        Context = context;
    }
}

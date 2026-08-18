using Hardened.Requests.Abstract.Outputs;
using Hardened.Templates.RazorBlade.Tests.Models;
using Hardened.Templates.RazorBlade.Tests.Support;
using Xunit;

namespace Hardened.Templates.RazorBlade.Tests;

/// <summary>
/// A view is rendered into a buffer and copied out asynchronously, so nothing writes
/// synchronously to a response body that refuses it.
/// </summary>
/// <remarks>
/// <para>
/// These drive <see cref="SynchronousWritesRejectedStream"/> rather than a <c>MemoryStream</c>.
/// That is the point of them: every other test in this project writes to a <c>MemoryStream</c>,
/// which accepts synchronous writes, so none of them could see the defect these cover.
/// </para>
/// <para>
/// What shipped: <c>WriteOutput</c> wrapped a <c>StreamWriter</c> around the response body with
/// the default 1 KiB character buffer, and RazorBlade's <c>WriteLiteral</c> is synchronous. A view
/// whose output passed 1 KiB flushed synchronously mid-render, Kestrel threw, and the client got
/// <c>200</c> with an empty body because the status had already gone out.
/// </para>
/// </remarks>
public class SynchronousWriteRegressionTests {

    /// <summary>Long enough that rendering it must cross StreamWriter's 1 KiB buffer.</summary>
    private static FortunePage LongPage() =>
        new(Enumerable.Range(1, 40)
            .Select(i => new Fortune(i, $"fortune number {i} with enough text to take up room"))
            .ToList());

    [Fact]
    public async Task AViewLargerThanTheWriterBufferStillRenders() {
        var context = Pipeline.ServerLikeContext(out var body);

        context.Response.ResponseValue = LongPage();

        await new Views.LongFortunes().WriteOutput(context);

        var rendered = Pipeline.Rendered(body);

        // The size is the whole point - assert it, so a smaller fixture cannot quietly stop
        // covering the case.
        Assert.True(
            rendered.Length > 1024,
            $"fixture must exceed StreamWriter's 1 KiB buffer to cover the regression, was {rendered.Length}");

        Assert.Contains("fortune number 1 with enough text", rendered);
        Assert.Contains("fortune number 40 with enough text", rendered);
    }

    /// <summary>
    /// The failure was partial output followed by a throw, so a body that is merely non-empty is
    /// not enough: the closing markup has to be there too.
    /// </summary>
    [Fact]
    public async Task TheWholeViewArrivesRatherThanTheFirstBufferful() {
        var context = Pipeline.ServerLikeContext(out var body);

        context.Response.ResponseValue = LongPage();

        await new Views.LongFortunes().WriteOutput(context);

        var rendered = Pipeline.Rendered(body);

        Assert.StartsWith("<ul>", rendered.TrimStart());
        Assert.EndsWith("</ul>", rendered.TrimEnd());
        Assert.Equal(40, rendered.Split("class=\"fortune\"").Length - 1);
    }

    /// <summary>A short view was never broken; it must not become so.</summary>
    [Fact]
    public async Task AViewSmallerThanTheWriterBufferStillRenders() {
        var context = Pipeline.ServerLikeContext(out var body);

        context.Response.ResponseValue = new FortunePage([new Fortune(1, "hello")]);

        await new Views.AttachedFortunes().WriteOutput(context);

        Assert.Contains("<li>1: hello</li>", Pipeline.Rendered(body));
    }

    /// <summary>
    /// The buffer is taken from the pool when one is registered, and rendering still works when
    /// none is - a container composed by hand has no shared-runtime module in it.
    /// </summary>
    [Fact]
    public async Task RenderingWorksWithoutAPooledBuffer() {
        var context = Pipeline.ServerLikeContext(out var body, withPool: false);

        context.Response.ResponseValue = LongPage();

        await new Views.LongFortunes().WriteOutput(context);

        Assert.Contains("fortune number 40", Pipeline.Rendered(body));
    }

    [Fact]
    public async Task NoByteOrderMarkReachesTheBody() {
        var context = Pipeline.ServerLikeContext(out var body);

        context.Response.ResponseValue = LongPage();

        await new Views.LongFortunes().WriteOutput(context);

        var bytes = body.ToArray();

        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "a BOM in the body shows up as stray characters ahead of the markup");
    }
}

using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Response compression under the application-wide default the fixture enables, and the
/// per-operation declarations that override it.
/// </summary>
[BasePath("/compression")]
public class CompressionController {

    /// <summary>
    /// Named apart from <c>MessagePackController.Reading</c> deliberately. A component is named by
    /// the type's own name, so two <c>Reading</c> records in two controllers were published as one
    /// schema and the last one written described both. HRDOA005 reports that now.
    /// </summary>
    public record Sample(string Sensor, int Value);

    private static List<Sample> Readings(int count) =>
        Enumerable.Range(0, count).Select(i => new Sample("sensor-" + i, i * 3)).ToList();

    /// <summary>Nothing declared, so the application-wide default applies.</summary>
    [Get("/readings")]
    public List<Sample> Readings() => Readings(20);

    /// <summary>Compressed only when the list is longer than three.</summary>
    [Get("/sized/{count}")]
    [Compress<ListLargerThan>(3)]
    public List<Sample> Sized(int count) => Readings(count);

    [Get("/brotli")]
    [Compress(Favor = CompressionType.Br)]
    public List<Sample> Brotli() => Readings(20);

    /// <summary>How an operation opts out of the application-wide default.</summary>
    [Get("/never")]
    [Compress<Never>]
    public List<Sample> Never() => Readings(20);

    [Get("/text")]
    [Produces("text/plain")]
    public string Text() => "plain text is on the list of media types the default rule compresses";

    [Get("/binary")]
    [Produces("application/octet-stream")]
    public byte[] Binary() => [1, 2, 3, 4, 5, 6, 7, 8];

    [Post("/echo")]
    public Sample Echo([FromBody] Sample sample) => sample;
}

public sealed class ListLargerThan : ICompressionPredicate {
    private readonly int _count;

    private ListLargerThan(int count) => _count = count;

    public static ICompressionPredicate Create(object[] args) => args is [int count]
        ? new ListLargerThan(count)
        : throw new ArgumentException("ListLargerThan takes one integer, the count above which the body is compressed.");

    public bool ShouldCompress(object value, IExecutionContext context) =>
        value is System.Collections.ICollection { Count: var n } && n > _count;
}

public sealed class Never : ICompressionPredicate {
    private static readonly Never _instance = new();

    public static ICompressionPredicate Create(object[] args) => _instance;

    public bool ShouldCompress(object value, IExecutionContext context) => false;
}

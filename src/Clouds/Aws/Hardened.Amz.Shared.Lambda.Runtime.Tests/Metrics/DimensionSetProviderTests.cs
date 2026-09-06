using Hardened.Amz.Shared.Lambda.Runtime.Metrics;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Metrics;

/// <summary>
/// Which tags become CloudWatch dimensions.
/// </summary>
/// <remarks>
/// A billing decision rather than a formatting one: CloudWatch charges per unique combination of
/// namespace, metric name and dimension values, so a promoted tag costs one custom metric per
/// distinct value it takes.
/// </remarks>
public class DimensionSetProviderTests {

    private static IReadOnlyCollection<Tuple<string, object>> Tags(params string[] names) =>
        names.Select(n => new Tuple<string, object>(n, "value-of-" + n)).ToList();

    /// <summary>
    /// The default promotes nothing, whatever is tagged.
    /// </summary>
    /// <remarks>
    /// It used to return every tag name, so a tag with unbounded cardinality — a path carrying an
    /// id, a customer identifier — became that many custom metrics. Nothing called
    /// <c>IMetricLogger.Tag</c>, so no deployment was affected and the default could still be
    /// narrowed; once the pipeline attaches a route it no longer can be.
    /// </remarks>
    [Fact]
    public void TheDefaultPromotesNothing() {
        var sets = new DimensionSetProvider().Get(Tags("http.route", "customer.id")).ToList();

        Assert.Equal(Array.Empty<string>(), Assert.Single(sets));
    }

    /// <summary>
    /// One empty dimension set, not zero sets. That is what the previous default already emitted for
    /// an application with no tags, so the JSON on the wire is unchanged for everyone not tagging.
    /// </summary>
    [Fact]
    public void TheDefaultEmitsOneEmptySetRatherThanNone() {
        Assert.Single(new DimensionSetProvider().Get(Tags()));
    }

    [Fact]
    public void NamedTagsArePromoted() {
        var provider = new TagDimensionSetProvider("http.route");

        var set = Assert.Single(provider.Get(Tags("http.route", "customer.id")).ToList());

        Assert.Equal(["http.route"], set);
    }

    /// <summary>
    /// Several sets is not one set of several tags: this asks for a per-route series and a
    /// per-route-and-status series, two groupings of the same measurement.
    /// </summary>
    [Fact]
    public void SeveralSetsGiveSeveralGroupings() {
        var provider = new TagDimensionSetProvider([
            ["http.route"],
            ["http.route", "http.response.status_code"]]);

        var sets = provider.Get(Tags("http.route", "http.response.status_code")).ToList();

        Assert.Equal(2, sets.Count);
        Assert.Equal(["http.route"], sets[0]);
        Assert.Equal(["http.route", "http.response.status_code"], sets[1]);
    }

    /// <summary>
    /// A set naming a tag this measurement does not carry is skipped. CloudWatch reads a dimension
    /// set as one key and rejects the whole log entry over a dimension with no value — which would
    /// take the measurements that were fine with it.
    /// </summary>
    [Fact]
    public void ASetIsSkippedWhenOneOfItsTagsIsAbsent() {
        var provider = new TagDimensionSetProvider([
            ["http.route"],
            ["http.route", "http.response.status_code"]]);

        var sets = provider.Get(Tags("http.route")).ToList();

        Assert.Equal(["http.route"], Assert.Single(sets));
    }

    /// <summary>
    /// Skipped, not trimmed. Trimming would emit a coarser metric under the same name, which reads
    /// as correct data and is harder to notice than a gap.
    /// </summary>
    [Fact]
    public void AnIncompleteSetIsNotTrimmedDownToWhatIsPresent() {
        var provider = new TagDimensionSetProvider("http.route", "http.response.status_code");

        Assert.Empty(provider.Get(Tags("http.route")));
    }

    /// <summary>
    /// Naming no tags gives one dimensionless series — the same thing the default provider does, so
    /// the degenerate configuration is not a surprise.
    /// </summary>
    [Fact]
    public void NamingNoTagsBehavesLikeTheDefault() {
        var sets = new TagDimensionSetProvider().Get(Tags("http.route")).ToList();

        Assert.Equal(Array.Empty<string>(), Assert.Single(sets));
    }
}

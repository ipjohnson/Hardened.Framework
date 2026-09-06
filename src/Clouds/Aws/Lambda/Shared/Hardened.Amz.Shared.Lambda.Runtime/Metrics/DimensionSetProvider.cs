using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Shared.Lambda.Runtime.Metrics;

/// <summary>
/// Decides which of a measurement's tags become CloudWatch dimensions, and in what groupings.
/// </summary>
/// <remarks>
/// <para>
/// This is a billing decision, not a formatting one. CloudWatch charges per unique combination of
/// namespace, metric name and dimension values, so every distinct value of every dimension is a
/// separate custom metric. A tag whose cardinality is unbounded - a path carrying an id, a customer
/// identifier, a correlation id - turns one metric into as many as there are values, and no one
/// notices until the bill arrives.
/// </para>
/// <para>
/// Which is why nothing is promoted by default. Register <see cref="TagDimensionSetProvider"/> to
/// opt specific tags in.
/// </para>
/// </remarks>
public interface IDimensionSetProvider {
    IEnumerable<IReadOnlyList<string>> Get(IReadOnlyCollection<Tuple<string, object>> tags);
}

/// <summary>
/// Promotes nothing. Every metric is emitted without dimensions.
/// </summary>
/// <remarks>
/// <para>
/// This used to return every tag name as one dimension set, so anything anyone tagged became a
/// dimension and arrived on the bill. Nothing in Hardened or Hardened.Amz called
/// <c>IMetricLogger.Tag</c>, so no deployment was affected - which is exactly why the default could
/// be changed here and could not be changed later. Once the pipeline starts attaching a route or a
/// status, narrowing this becomes a change to somebody's dashboards.
/// </para>
/// <para>
/// The wire format is unchanged for an application that tags nothing: an empty dimension set is what
/// the previous default already produced when there were no tags.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class DimensionSetProvider : IDimensionSetProvider {
    private static readonly IReadOnlyList<string>[] _noDimensions = [Array.Empty<string>()];

    public IEnumerable<IReadOnlyList<string>> Get(IReadOnlyCollection<Tuple<string, object>> tags) {
        return _noDimensions;
    }
}

/// <summary>
/// Promotes the tags an application asks for, in the groupings it asks for.
/// </summary>
/// <remarks>
/// <para>
/// Registered in place of the default, naming the tags whose cardinality the bill can carry:
/// </para>
/// <code>
/// // One series per route.
/// services.AddSingleton&lt;IDimensionSetProvider&gt;(
///     new TagDimensionSetProvider("http.route"));
///
/// // Or several groupings of the same measurement at once.
/// services.AddSingleton&lt;IDimensionSetProvider&gt;(
///     new TagDimensionSetProvider([
///         ["http.route"],
///         ["http.route", "http.response.status_code"]]));
/// </code>
/// <para>
/// Several sets is not the same as one set of several tags. <c>["route"], ["route", "status"]</c>
/// gives a per-route series and a per-route-and-status series. <c>["route", "status"]</c> alone gives
/// only the second, and a per-route number then has to be aggregated back out of it.
/// </para>
/// </remarks>
public class TagDimensionSetProvider : IDimensionSetProvider {
    private readonly IReadOnlyList<IReadOnlyList<string>> _dimensionSets;

    /// <summary>
    /// One dimension set, from the named tags.
    /// </summary>
    public TagDimensionSetProvider(params string[] tagNames)
        : this([tagNames]) {
    }

    /// <summary>
    /// Several dimension sets, each a grouping of the same measurement.
    /// </summary>
    /// <remarks>
    /// Not <c>params</c>, deliberately: two <c>params</c> constructors make <c>new
    /// TagDimensionSetProvider()</c> ambiguous, and the nesting reads as what it is either way -
    /// <c>new TagDimensionSetProvider([["route"], ["route", "status"]])</c>.
    /// </remarks>
    public TagDimensionSetProvider(IEnumerable<IReadOnlyList<string>> dimensionSets) {
        _dimensionSets = dimensionSets.ToList();
    }

    public IEnumerable<IReadOnlyList<string>> Get(IReadOnlyCollection<Tuple<string, object>> tags) {
        foreach (var dimensionSet in _dimensionSets) {
            // All or nothing. CloudWatch reads a dimension set as a single key, and a set naming a
            // tag this measurement does not carry would declare a dimension with no value - which
            // invalidates the whole log entry, taking the measurements that were fine with it.
            //
            // Skipped rather than trimmed: trimming would quietly emit a coarser metric under the
            // same name, which is harder to notice than emitting nothing.
            if (AllPresent(dimensionSet, tags)) {
                yield return dimensionSet;
            }
        }
    }

    private static bool AllPresent(
        IReadOnlyList<string> dimensionSet, IReadOnlyCollection<Tuple<string, object>> tags) {
        foreach (var name in dimensionSet) {
            var found = false;

            foreach (var tag in tags) {
                if (tag.Item1 == name) {
                    found = true;

                    break;
                }
            }

            if (!found) {
                return false;
            }
        }

        return true;
    }
}

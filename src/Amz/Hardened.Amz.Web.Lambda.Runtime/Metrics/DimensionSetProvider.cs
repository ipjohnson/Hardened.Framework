using Amazon.CloudWatch.EMF.Model;

namespace Hardened.Amz.Web.Lambda.Runtime.Metrics;

public interface IDimensionSetProvider
{
    IEnumerable<DimensionSet> Get(IReadOnlyCollection<Tuple<string, object>> tags);
}

public class DimensionSetProvider : IDimensionSetProvider
{
    public IEnumerable<DimensionSet> Get(IReadOnlyCollection<Tuple<string, object>> tags)
    {
        var dimensionSet = new DimensionSet();

        foreach (var tag in tags)
        {
            dimensionSet.AddDimension(tag.Item1, tag.Item2.ToString() ?? "");
        }

        yield return dimensionSet;
    }
}
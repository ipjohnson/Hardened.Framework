using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Web.Lambda.Runtime.Metrics;

public interface IDimensionSetProvider {
    IEnumerable<IReadOnlyList<string>> Get(IReadOnlyCollection<Tuple<string, object>> tags);
}

[SingletonService(Using = RegistrationType.Try)]
public class DimensionSetProvider : IDimensionSetProvider {
    public IEnumerable<IReadOnlyList<string>> Get(IReadOnlyCollection<Tuple<string, object>> tags) {
        yield return tags.Select(t => t.Item1).ToList();
    }
}

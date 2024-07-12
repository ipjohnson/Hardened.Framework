namespace Hardened.SourceGenerator.DependencyInjection;

public class ServiceModelComparer : IEqualityComparer<DependencyInjectionIncrementalGenerator.ServiceModel>
{
    public bool Equals(DependencyInjectionIncrementalGenerator.ServiceModel x, DependencyInjectionIncrementalGenerator.ServiceModel y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null)) return false;
        if (ReferenceEquals(y, null)) return false;

        return x.Equals(y);
    }

    public int GetHashCode(DependencyInjectionIncrementalGenerator.ServiceModel obj)
    {
        return obj.GetHashCode();
    }
}
namespace Hardened.Amz.Canaries.Runtime.Discovery;

public interface ICanaryTypeRegistration
{
    IEnumerable<Type> ScanTypes();
}

public class CanaryTypeRegistration : ICanaryTypeRegistration
{
    private readonly Type[] _types;

    public CanaryTypeRegistration(params Type[] types)
    {
        _types = types;
    }

    public IEnumerable<Type> ScanTypes()
    {
        return _types;
    }
}
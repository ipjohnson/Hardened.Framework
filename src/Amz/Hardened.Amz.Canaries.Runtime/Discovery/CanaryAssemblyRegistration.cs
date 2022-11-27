using Hardened.Amz.Canaries.Runtime.Services;
using Hardened.Shared.Runtime.Attributes;
using System.Reflection;

namespace Hardened.Amz.Canaries.Runtime.Discovery;

[Expose]
[Singleton]
public class CanaryAssemblyRegistration : ICanaryTypeRegistration
{
    private readonly Assembly _assembly;

    public CanaryAssemblyRegistration(IXunitAssemblyUnderTest assemblyUnderTest)
    {
        _assembly = assemblyUnderTest.AssemblyUnderTest;
    }

    public IEnumerable<Type> ScanTypes()
    {
        return _assembly.ExportedTypes;
    }
}
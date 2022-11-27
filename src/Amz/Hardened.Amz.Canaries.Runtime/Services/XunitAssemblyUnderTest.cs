using System.Reflection;

namespace Hardened.Amz.Canaries.Runtime.Services;

public interface IXunitAssemblyUnderTest
{
    Assembly AssemblyUnderTest { get; }
}

public class XunitAssemblyUnderTest : IXunitAssemblyUnderTest
{
    public XunitAssemblyUnderTest(Assembly assemblyUnderTest)
    {
        AssemblyUnderTest = assemblyUnderTest;
    }

    public Assembly AssemblyUnderTest { get; }
}
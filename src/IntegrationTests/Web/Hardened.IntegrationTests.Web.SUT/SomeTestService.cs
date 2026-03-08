using DependencyModules.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.SUT;

public interface ISomeTestService { }

[TransientService]
internal class SomeTestService : ISomeTestService { }
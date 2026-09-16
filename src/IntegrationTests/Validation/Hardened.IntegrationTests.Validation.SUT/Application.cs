using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Validation.SUT;

/// <summary>
/// The application module, and the only thing in this project that is wired to anything.
/// </summary>
/// <remarks>
/// It carries <c>[HardenedModule]</c> and no <c>[DependencyModule]</c>, which is every Hardened
/// application. <c>ValidationModules.SourceGenerator</c> finds it by that attribute and completes
/// it with a partial that adds this assembly's validators to
/// <c>DependencyRegistry&lt;Application&gt;</c>, so composing the module is the whole of the
/// wiring.
/// </remarks>
[HardenedModule]
public partial class Application;

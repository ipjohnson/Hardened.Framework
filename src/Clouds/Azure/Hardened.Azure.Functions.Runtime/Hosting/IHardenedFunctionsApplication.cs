using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Runtime.Hosting;

/// <summary>
/// What the generator adds to an application so the worker can find its functions.
/// </summary>
/// <remarks>
/// <para>
/// The worker discovers functions through two seams, <c>IFunctionMetadataProvider</c> and
/// <c>IFunctionExecutor</c>, and Hardened.Azure.Functions.SourceGenerator implements both from the
/// shims it writes. Something has to register that pair with the worker, and it cannot be this
/// package: the types are generated into the application, which this package cannot name.
/// </para>
/// <para>
/// So the generator makes the application implement this, with the two registrations in
/// <see cref="ConfigureFunctionsWorker"/>, and <c>UseHardened&lt;TApplication&gt;</c> calls it. An
/// explicit interface rather than the worker's own <c>IAutoConfigureStartup</c>, which the worker
/// finds by reflecting over every type in the entry assembly: a generic constraint is checked by
/// the compiler, costs nothing at start, and survives a trimmed publish without a
/// <c>DynamicDependency</c>.
/// </para>
/// </remarks>
public interface IHardenedFunctionsApplication {
    /// <summary>Registers the generated metadata provider and executor with the worker.</summary>
    void ConfigureFunctionsWorker(IServiceCollection services);
}

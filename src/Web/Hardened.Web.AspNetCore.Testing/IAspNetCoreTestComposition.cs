using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;

namespace Hardened.Web.AspNetCore.Testing;

/// <summary>
/// What a test project's <c>Program.cs</c> puts around <c>UseHardened()</c>, so the test host
/// runs the pipeline the application runs.
/// </summary>
/// <remarks>
/// A type rather than a lambda because an attribute argument has to be. The default composition
/// is <c>app.UseHardened()</c> and nothing else; a project whose <c>Program.cs</c> puts
/// authentication in front and static files behind writes a composition of two methods that do
/// the same, and names it in <c>[assembly: AspNetCoreTesting(typeof(...))]</c>.
/// </remarks>
public interface IAspNetCoreTestComposition {

    /// <summary>Before <c>Build</c>: the services and host configuration <c>Program.cs</c> adds.</summary>
    void Configure(WebApplicationBuilder builder);

    /// <summary>
    /// After <c>Build</c>: the middleware, in the order <c>Program.cs</c> has it. Calls
    /// <c>UseHardened()</c> itself, where <c>Program.cs</c> does.
    /// </summary>
    void Configure(WebApplication app);
}

/// <summary>The composition with nothing around Hardened: <c>app.UseHardened()</c> alone.</summary>
public sealed class DefaultAspNetCoreTestComposition : IAspNetCoreTestComposition {

    public void Configure(WebApplicationBuilder builder) {
    }

    public void Configure(WebApplication app) => app.UseHardened();
}

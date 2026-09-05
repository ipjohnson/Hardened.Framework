using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// Answers a conditional GET at every GET handler in the application.
///
/// <code>
/// [HardenedModule]
/// [Enable&lt;ConditionalGet&gt;]
/// [KestrelRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>Opt in.</b> Nothing answers a 304 until this is written or an operation carries
/// <c>[ConditionalGet]</c>. Every GET handler then tags what it sends when it wrote no validator
/// of its own, which holds each response back for a hash, and answers a caller that holds the
/// tag with a 304 and no body. That is a cost on every GET in the application, paid for
/// bandwidth, and this is the declaration that says the application wants it.
/// </para>
/// <para>
/// An operation carrying <c>[ConditionalGet]</c> is left to its own declaration. The default
/// installed here is a global provider that stands down for any handler whose metadata carries
/// one, so explicit beats convention without the registration having to say so.
/// </para>
/// <para>
/// A handler that streams gets nothing from this either. Tagging a response means holding it
/// back until it is all there, and a stream held back until it is all there is not a stream: a
/// server-sent event feed enabled application-wide arrived as one packet, after the last event,
/// with an <c>ETag</c> on it. <c>IExecutionRequestHandlerInfo.StreamsResponse</c> is what the
/// provider reads to leave those alone.
/// </para>
/// </summary>
/// <remarks>
/// No module attribute is generated for this class, so that <c>[ConditionalGet]</c> can be the
/// per-operation attribute: DependencyModules would otherwise emit a <c>ConditionalGetAttribute</c>
/// of its own under the same name. The module is applied through <c>[Enable&lt;T&gt;]</c>, which
/// the generator turns into <c>AddModule(new ConditionalGet())</c> and needs no attribute for.
/// </remarks>
[DependencyModule(GenerateAttribute = false)]
public partial class ConditionalGet : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.AddGlobalFilter(
            new ConditionalGetAttribute(),
            when: handlerInfo =>
                !ConditionalGetAttribute.Declares(handlerInfo) && !handlerInfo.StreamsResponse);
    }

    /// <summary>
    /// Every install is the same install, so writing this twice registers one default.
    /// </summary>
    public override bool Equals(object? obj) => obj is ConditionalGet;

    public override int GetHashCode() => typeof(ConditionalGet).GetHashCode();
}

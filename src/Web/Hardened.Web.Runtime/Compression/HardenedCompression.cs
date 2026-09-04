using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Compression;

/// <summary>
/// Compresses every response the configured media-type rule admits, for every client that accepts
/// it.
///
/// <code>
/// [HardenedModule]
/// [Enable&lt;HardenedCompression&gt;]
/// [KestrelRuntime]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>Opt in.</b> Nothing is compressed until this is written or an operation carries
/// <c>[Compress]</c>. gzip is offered first and Brotli behind it, both at the fastest level; the
/// order, the level and the media types are set with
/// <c>services.ConfigureCompression</c>. Request decompression is not part of this: a compressed
/// request body is decoded for every application, whether or not this is enabled.
/// </para>
/// <para>
/// An operation carrying <c>[Compress]</c> or <c>[Compress&lt;T&gt;]</c> is left to its own
/// declaration. The default installed here is a global provider that stands down for any handler
/// whose metadata carries one, so explicit beats convention without the registration having to
/// say so.
/// </para>
/// <para>
/// Do not add ASP.NET Core's own response compression middleware beside this. If it is present it
/// sees the <c>Content-Encoding</c> this writes and stands down, so nothing breaks, but it is
/// work for nothing.
/// </para>
/// </summary>
[DependencyModule]
public partial class HardenedCompression : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.AddGlobalFilter(
            new CompressAttribute(),
            when: handlerInfo => !CompressAttribute.Declares(handlerInfo));
    }

    /// <summary>
    /// Every install is the same install, so writing this twice registers one default.
    /// </summary>
    public override bool Equals(object? obj) => obj is HardenedCompression;

    public override int GetHashCode() => typeof(HardenedCompression).GetHashCode();
}

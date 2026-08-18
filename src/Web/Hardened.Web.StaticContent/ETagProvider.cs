using System.Security.Cryptography;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.StaticContent;

public interface IETagProvider {
    string GenerateETag(byte[] content);
}

[SingletonService(Using = RegistrationType.Try)]
/// <remarks>
/// SHA-256 rather than MD5. The value is opaque and any hash would serve as a validator, so the
/// choice looks free - but <c>MD5.Create()</c> throws outright on a FIPS-enforcing host, which
/// would take the static content path down on its first request rather than degrade. The build task
/// already hashes with SHA-256; this is the same decision on the side that reads a directory.
/// </remarks>
public class ETagProvider : IETagProvider {
    private readonly IItemPool<SHA256> _hashPool;

    public ETagProvider(IItemPool<SHA256> hashPool) {
        _hashPool = hashPool;
    }

    public string GenerateETag(byte[] content) {
        using var rental = _hashPool.Get();

        return Convert.ToBase64String(rental.Item.ComputeHash(content));
    }
}
using System.Security.Cryptography;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.StaticContent.Tests;

public class TestHashPool : ItemPool<SHA256> {
    public TestHashPool() : base(SHA256.Create, _ => { }, hash => hash.Dispose()) { }
}

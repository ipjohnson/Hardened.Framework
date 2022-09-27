using System.Security.Cryptography;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.Runtime.Tests.StaticContent
{
    public class TestMD5Pool : ItemPool<MD5>
    {
        public TestMD5Pool() : base(MD5.Create, _ => {}, md5 => md5.Dispose())
        {
        }
    }
}

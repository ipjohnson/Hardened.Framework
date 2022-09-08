using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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

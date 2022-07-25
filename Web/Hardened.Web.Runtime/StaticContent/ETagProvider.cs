using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Web.Runtime.StaticContent
{
    public interface IETagProvider
    {
        string GenerateETag(byte[] content);
    }

    public class ETagProvider : IETagProvider
    {
        private MD5 _md5 = MD5.Create();

        public string GenerateETag(byte[] content)
        {
            var hash = _md5.ComputeHash(content);

            return Convert.ToBase64String(hash);
        }
    }
}

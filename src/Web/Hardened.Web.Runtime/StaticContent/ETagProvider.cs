using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.Runtime.StaticContent
{
    public interface IETagProvider
    {
        string GenerateETag(byte[] content);
    }

    public class ETagProvider : IETagProvider
    {
        private readonly IItemPool<MD5> _md5Pool;

        public ETagProvider(IItemPool<MD5> md5Pool)
        {
            _md5Pool = md5Pool;
        }

        public string GenerateETag(byte[] content)
        {
            using var md5Rental = _md5Pool.Get();

            var hashBytes = md5Rental.Item.ComputeHash(content);

            return Convert.ToBase64String(hashBytes);
        }
    }
}

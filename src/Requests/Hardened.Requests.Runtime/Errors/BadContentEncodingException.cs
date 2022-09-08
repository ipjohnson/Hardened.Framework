using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Runtime.Errors
{
    public class BadContentEncodingException : Exception
    {
        public BadContentEncodingException(string contentEncoding) : base(
            $"{contentEncoding} is not a supported Content-Encoding")
        {

        }
    }
}

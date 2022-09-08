using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Web.Runtime.CacheControl;

namespace Hardened.Web.Runtime.Attributes
{
    public class CacheControlAttribute : Attribute
    {
        public int MaxAge { get; set; } = 0;

        public CacheControlEnum Type { get; set; } = CacheControlEnum.MaxAge | CacheControlEnum.Public;
    }
}

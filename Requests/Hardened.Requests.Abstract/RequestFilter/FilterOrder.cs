using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.RequestFilter
{
    public static class FilterOrder
    {
        public const int Serialization = 5;

        public const int DefaultValue = 1000;

        public const int EndPointHandlers = DefaultValue * 2;

        public  const int EndPointInvoke = DefaultValue * 2;
    }
}

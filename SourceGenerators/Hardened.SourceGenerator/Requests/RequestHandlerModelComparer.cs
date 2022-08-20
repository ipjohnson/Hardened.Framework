using System;
using System.Collections.Generic;
using System.Text;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests
{
    public class RequestHandlerModelComparer : IEqualityComparer<RequestHandlerModel>
    {
        public bool Equals(RequestHandlerModel x, RequestHandlerModel y)
        {
            var equalValue = x.Equals(y);
            
            return equalValue;
        }

        public int GetHashCode(RequestHandlerModel obj)
        {
            return obj.GetHashCode();
        }
    }
}

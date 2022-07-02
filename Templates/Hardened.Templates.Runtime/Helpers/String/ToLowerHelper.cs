using System;
using System.Collections.Generic;
using System.Text;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class ToLowerHelper : BaseStringHelper
    {
        protected override object AugmentString(string value)
        {
            return value.ToLowerInvariant();
        }
    }
}

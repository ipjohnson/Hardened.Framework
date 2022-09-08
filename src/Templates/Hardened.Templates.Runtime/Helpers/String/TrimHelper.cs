using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class TrimHelper : BaseStringHelper
    {
        protected override object AugmentString(string stringValue)
        {
            return stringValue.Trim();
        }
    }
}

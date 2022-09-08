using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class StartsWithHelper : BaseStringEvaluationHelper
    {
        protected override bool EvaluateStrings(string oneString, string twoString)
        {
            if (oneString == string.Empty || twoString == string.Empty)
            {
                return false;
            }

            return oneString.StartsWith(twoString);
        }
    }
}

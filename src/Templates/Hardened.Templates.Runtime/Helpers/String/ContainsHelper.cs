using System;
using System.Collections.Generic;
using System.Text;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class ContainsHelper : BaseStringEvaluationHelper
    {

        protected override bool EvaluateStrings(string oneString, string twoString)
        {
            if (oneString == string.Empty || twoString == string.Empty)
            {
                return false;
            }

            return oneString.Contains(twoString);
        }
    }
}

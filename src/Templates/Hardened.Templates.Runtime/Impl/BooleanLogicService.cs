using System.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl
{
    public class BooleanLogicService : IBooleanLogicService
    {
        public bool IsTrueValue(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is int intValue)
            {
                return intValue > 0;
            }

            if (value is string stringValue)
            {
                return stringValue.Length > 0;
            }

            if (value is ICollection listValue)
            {
                return listValue.Count > 0;
            }
            
            return true;
        }
    }
}

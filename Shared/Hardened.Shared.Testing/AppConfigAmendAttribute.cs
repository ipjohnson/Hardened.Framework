using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing
{
    public class AppConfigAmendAttribute : Attribute
    {
        public AppConfigAmendAttribute(Type configType, string propertyName, object value)
        {
            ConfigType = configType;
            PropertyName = propertyName;
            Value = value;
        }

        public Type ConfigType { get; }

        public string PropertyName { get; }

        public object Value { get; }
    }
}

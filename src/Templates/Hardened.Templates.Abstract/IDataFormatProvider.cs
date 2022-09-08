using System;
using System.Collections.Generic;
using System.Text;

namespace Hardened.Templates.Abstract
{
    public interface IDataFormatProvider
    {
        void ProvideFormatters(IDictionary<Type, FormatDataFunc> formatter);
    }
}

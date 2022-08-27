using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.Serializer
{
    public interface IStringConverterService
    {
        T ParseRequired<T>(string value, string valueName);

        T ParseWithDefault<T>(string value, string valueName, T defaultValue);

        T? ParseOptional<T>(string value, string valueName);
    }
}

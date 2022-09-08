using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.Serializer
{
    public interface IStringConverter
    {
        Type ConvertType { get; }

        T Convert<T>(string value);
    }
}

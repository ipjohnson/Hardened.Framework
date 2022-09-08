using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Abstract.Execution
{
    public interface IKnownServices
    {
        IContextSerializationService ContextSerializationService { get; }

        IStringConverterService StringConverterService { get; }
    }
}

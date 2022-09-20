using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Attributes;
using Newtonsoft.Json;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl
{
    public interface ISharedSerializer
    {
        JsonSerializer Serializer { get; }
    }

    [Expose]
    [Singleton]
    public class SharedSerializer : ISharedSerializer
    {
        public JsonSerializer Serializer { get; } = JsonSerializer.CreateDefault();
    }
}

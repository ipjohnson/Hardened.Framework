using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hardened.Requests.Runtime.Configuration
{
    public interface IJsonSerializerConfiguration
    {
        JsonSerializerOptions? SerializeOptions { get; }

        JsonSerializerOptions? DeSerializerOptions { get; }
    }

    public class JsonSerializerConfiguration : IJsonSerializerConfiguration
    {
        public JsonSerializerOptions? SerializeOptions { get; set; }
        
        public JsonSerializerOptions? DeSerializerOptions { get; set; }
    }
}

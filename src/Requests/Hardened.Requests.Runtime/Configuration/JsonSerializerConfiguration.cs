using System.Text.Json;

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

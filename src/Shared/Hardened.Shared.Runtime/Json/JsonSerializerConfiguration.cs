using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Json;

public interface IJsonSerializerConfiguration
{
    JsonSerializerOptions Options { get; }
}
public class JsonSerializerConfiguration : IJsonSerializerConfiguration
{
    public JsonSerializerOptions Options { get; set; } = DefaultConfiguration();

    private static JsonSerializerOptions DefaultConfiguration()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            AllowTrailingCommas = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
    }
}

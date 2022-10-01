using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Json;

public interface IJsonSerializerConfiguration
{
    JsonSerializerOptions Options { get; }
}
public class JsonSerializerConfiguration : IJsonSerializerConfiguration
{
    public JsonSerializerOptions Options { get; set; } = new(JsonSerializerDefaults.Web);
}

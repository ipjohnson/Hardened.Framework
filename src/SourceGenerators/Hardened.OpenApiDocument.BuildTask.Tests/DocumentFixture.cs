using System.IO.Compression;
using System.Text;

namespace Hardened.OpenApiDocument.BuildTask.Tests;

/// <summary>
/// A compact document holding the cases a writer gets wrong: <c>$ref</c> values, a path key with a
/// token, strings that read as booleans, numbers or null, a multi-line description with quotes and
/// a backslash, non-ASCII text, empty objects and arrays, a streamed response with an
/// <c>itemSchema</c>, and the 3.1 spellings of an exclusive bound and a nullable type.
/// </summary>
internal static class DocumentFixture {

    public const string Compact =
        "{\"openapi\":\"3.2.0\",\"info\":{\"title\":\"Fixture: yes\",\"version\":\"1.0\"," +
        "\"description\":\"Line one\\nLine two \\\"quoted\\\" \\\\ back\"}," +
        "\"paths\":{\"/things/{id}\":{\"get\":{\"operationId\":\"getThing\",\"tags\":[\"Things\"]," +
        "\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"," +
        "\"enum\":[\"yes\",\"no\",\"null\",\"1e3\",\"007\",\"on\",\"true\",\"-1\",\"0x1F\",\".inf\",\"caf\u00e9\",\"a b\",\"plain-ok\",\"x/y.z\",\"\"]}}]," +
        "\"responses\":{\"200\":{\"description\":\"caf\u00e9 \u2615 \u00fcn\u00efcode\",\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Thing\"}}}}," +
        "\"404\":{\"description\":\"Missing\"}}}}," +
        "\"/events\":{\"get\":{\"operationId\":\"events\",\"responses\":{\"200\":{\"description\":\"Stream\"," +
        "\"content\":{\"text/event-stream\":{\"schema\":{\"type\":\"string\"},\"itemSchema\":{\"$ref\":\"#/components/schemas/Thing\"}}}}}}}}," +
        "\"components\":{\"schemas\":{\"Thing\":{\"type\":\"object\",\"required\":[\"id\"],\"properties\":{" +
        "\"id\":{\"type\":\"string\"}," +
        "\"count\":{\"type\":[\"integer\",\"null\"],\"exclusiveMinimum\":0,\"exclusiveMaximum\":100.5,\"default\":null}," +
        "\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":0}," +
        "\"empty\":{\"type\":\"object\",\"properties\":{}}," +
        "\"flags\":{\"type\":\"array\",\"items\":{}}}}},\"x-empty-list\":[]}}";

    public static byte[] Compressed(string document = Compact) {
        var bytes = Encoding.UTF8.GetBytes(document);

        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}

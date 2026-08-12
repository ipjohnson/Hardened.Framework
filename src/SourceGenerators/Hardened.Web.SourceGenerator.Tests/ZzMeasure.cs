using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

// TEMPORARY measurement harness. Delete after reading the numbers.
public class ZzMeasure {

    private const string Out =
        "/private/tmp/claude-501/-Users-ianjohnson-Hardened/8e675e55-cc1a-45b3-be78-de456f811fde/scratchpad/routing-size.txt";

    private static readonly string[] Nouns = [
        "orders", "customers", "invoices", "shipments", "products", "payments", "refunds",
        "subscriptions", "accounts", "addresses", "carts", "catalogs", "discounts", "inventory",
        "notifications", "organizations", "permissions", "quotes", "returns", "sessions",
        "suppliers", "taxes", "users", "vendors", "warehouses"
    ];

    private static string ResourceName(int i) =>
        i < Nouns.Length ? Nouns[i] : Nouns[i % Nouns.Length] + (i / Nouns.Length);

    private static string Capitalise(string value) =>
        char.ToUpperInvariant(value[0]) + value.Substring(1);

    private static string Source(int resourceCount) {
        var source = new StringBuilder();

        source.AppendLine("using Hardened.Web.Runtime.Attributes;");
        source.AppendLine("using Hardened.Shared.Runtime.Attributes;");
        source.AppendLine();
        source.AppendLine("namespace TestApp;");
        source.AppendLine();
        source.AppendLine("[HardenedModule]");
        source.AppendLine("public partial class TestApplication { }");
        source.AppendLine();

        for (var i = 0; i < resourceCount; i++) {
            var name = ResourceName(i);

            source.AppendLine($"public class {Capitalise(name)}Controller {{");
            source.AppendLine($"    [Get(\"/api/v1/{name}\")] public string List{i}() => \"\";");
            source.AppendLine($"    [Post(\"/api/v1/{name}\")] public string Create{i}() => \"\";");
            source.AppendLine($"    [Get(\"/api/v1/{name}/{{id}}\")] public string Get{i}(string id) => id;");
            source.AppendLine($"    [Put(\"/api/v1/{name}/{{id}}\")] public string Put{i}(string id) => id;");
            source.AppendLine($"    [Patch(\"/api/v1/{name}/{{id}}\")] public string Patch{i}(string id) => id;");
            source.AppendLine($"    [Delete(\"/api/v1/{name}/{{id}}\")] public string Delete{i}(string id) => id;");
            source.AppendLine($"    [Get(\"/api/v1/{name}/{{id}}/history\")] public string History{i}(string id) => id;");
            source.AppendLine($"    [Get(\"/api/v1/{name}/{{id}}/history/{{e}}\")] public string HistoryItem{i}(string id, string e) => id;");
            source.AppendLine("}");
        }

        return source.ToString();
    }

    private static (int table, int handlers, int methods) IlSplit(GeneratorResult result) {
        using var stream = new MemoryStream();

        if (!result.Compilation.Emit(stream).Success) {
            return (-1, -1, -1);
        }

        stream.Position = 0;

        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var table = 0;
        var handlers = 0;
        var methods = 0;

        foreach (var typeHandle in reader.TypeDefinitions) {
            var type = reader.GetTypeDefinition(typeHandle);
            var isTable = reader.GetString(type.Name).Contains("RoutingTable");

            foreach (var methodHandle in type.GetMethods()) {
                var method = reader.GetMethodDefinition(methodHandle);

                if (method.RelativeVirtualAddress == 0) {
                    continue;
                }

                var size = pe.GetMethodBody(method.RelativeVirtualAddress).GetILContent().Length;

                if (isTable) {
                    table += size;
                    methods++;
                }
                else {
                    handlers += size;
                }
            }
        }

        return (table, handlers, methods);
    }

    [Fact]
    public void Measure() {
        var report = new StringBuilder();

        report.AppendLine("routes | table methods | ROUTING IL | handler IL");

        foreach (var resourceCount in new[] { 1, 10, 25, 50 }) {
            var result = GeneratorTestHarness.Run(
                new Dictionary<string, string> { ["Test.cs"] = Source(resourceCount) },
                new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
                GeneratedRoutingTable.Anchors,
                assemblyName: "SizeProbe" + resourceCount);

            if (result.Errors.Any()) {
                report.AppendLine($"{resourceCount}: ERRORS {string.Join("; ", result.Errors.Take(2))}");

                continue;
            }

            var (table, handlers, methods) = IlSplit(result);

            report.AppendLine($"{resourceCount * 8,6} | {methods,13} | {table,10} | {handlers,10}");
        }

        // Confirm the static-route leaves now return a cached record.
        var single = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Source(1) },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            GeneratedRoutingTable.Anchors,
            assemblyName: "ShapeProbe");

        var routing = single.GeneratedSources
            .Where(pair => pair.Value.Contains("class RoutingTable"))
            .Select(pair => pair.Value)
            .First();

        report.AppendLine();
        report.AppendLine("_info fields (cached static leaves): " + Count(routing, "_info"));
        report.AppendLine("new RequestHandlerInfo sites:        " + Count(routing, "new RequestHandlerInfo"));

        File.WriteAllText(Out, report.ToString());
    }

    private static int Count(string haystack, string needle) {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += needle.Length;
        }

        return count;
    }
}

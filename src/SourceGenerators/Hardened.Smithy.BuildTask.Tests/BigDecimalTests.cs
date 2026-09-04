using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// The two prelude shapes with no exact C# type, and what the build says about them.
/// </summary>
/// <remarks>
/// <para>
/// <c>BigDecimal</c> used to become <c>double</c>. Both narrow, and they narrow differently: a
/// double loses the value's exactness at any magnitude, so 19.99 stops being 19.99 and money stops
/// adding up, while a decimal is exact and runs out at 28 significant digits. A model reaching for
/// <c>BigDecimal</c> has almost always reached for exactness rather than for range.
/// </para>
/// <para>
/// H-31 is the other half: three <c>BigDecimal</c> members produced three byte-identical warnings
/// naming none of them, so the count told you how many and nothing told you which.
/// </para>
/// </remarks>
public class BigDecimalTests {

    private static string Model(string members, string shapes = "") =>
        $$"""
          { "smithy": "2.0", "shapes": {
              "com.example#Svc": {
                "type": "service", "version": "1",
                "operations": [ { "target": "com.example#Charge" } ] },
              "com.example#Charge": {
                "type": "operation",
                "input": { "target": "com.example#Money" },
                "traits": {
                  "smithy.api#http": { "method": "POST", "uri": "/charges", "code": 200 } } },
              "com.example#Money": {
                "type": "structure",
                "members": { {{members}} } }{{shapes}} } }
          """;

    private static SchemaModel Parse(string members, out List<string> diagnostics, string shapes = "") {
        diagnostics = new List<string>();

        var model = SmithySpecParser.Parse(Model(members, shapes), "money", diagnostics);

        Assert.NotNull(model);

        return Assert.Single(model!.Schemas, s => s.Name == "Money");
    }

    [Fact]
    public void ABigDecimalMemberReachesCSharpDecimal() {
        var schema = Parse("""
            "amount": { "target": "smithy.api#BigDecimal" }
            """, out _);

        var amount = Assert.Single(schema.Properties);

        Assert.Equal("number", amount.Type);
        Assert.Equal("decimal", amount.Format);
        Assert.Equal("decimal", TypeMapper.MapPropertyToCSharpType(amount));
    }

    /// <summary>
    /// H-20 from the 0.19.0-rc1000 trial, the Smithy half. A list or a map whose member targets
    /// <c>BigDecimal</c> dropped the format a bare member kept, so the build warned that the
    /// member "becomes decimal" while generating <c>double</c>.
    /// </summary>
    [Fact]
    public void AListOfBigDecimalIsAListOfDecimal() {
        var schema = Parse("""
            "amounts": { "target": "com.example#Amounts" }
            """, out _, """
            , "com.example#Amounts": {
                "type": "list",
                "member": { "target": "smithy.api#BigDecimal" } }
            """);

        var amounts = Assert.Single(schema.Properties);

        Assert.Equal("decimal", amounts.ArrayItemsFormat);
        Assert.Equal("List<decimal>", TypeMapper.MapPropertyToCSharpType(amounts));
    }

    [Fact]
    public void AMapOfBigDecimalIsADictionaryOfDecimal() {
        var schema = Parse("""
            "quotes": { "target": "com.example#Quotes" }
            """, out _, """
            , "com.example#Quotes": {
                "type": "map",
                "key": { "target": "smithy.api#String" },
                "value": { "target": "smithy.api#BigDecimal" } }
            """);

        var quotes = Assert.Single(schema.Properties);

        Assert.Equal("decimal", quotes.DictionaryValueFormat);
        Assert.Equal("Dictionary<string, decimal>", TypeMapper.MapPropertyToCSharpType(quotes));
    }

    /// <summary>Float and Double are exact mappings and say nothing.</summary>
    [Theory]
    [InlineData("smithy.api#Float", "float")]
    [InlineData("smithy.api#Double", "double")]
    public void AnExactlyMappedShapeIsSilent(string shape, string csType) {
        var schema = Parse($$"""
            "amount": { "target": "{{shape}}" }
            """, out var diagnostics);

        Assert.Equal(csType, TypeMapper.MapPropertyToCSharpType(Assert.Single(schema.Properties)));
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// The narrowing is still reported, because 28 digits is not arbitrarily many - and the
    /// message says what it became rather than that no type exists.
    /// </summary>
    [Fact]
    public void TheNarrowingIsReportedAndNamesWhatItBecame() {
        Parse("""
            "amount": { "target": "smithy.api#BigDecimal" }
            """, out var diagnostics);

        var warning = Assert.Single(diagnostics);

        Assert.Contains("'Money.amount'", warning);
        Assert.Contains("BigDecimal", warning);
        Assert.Contains("decimal", warning);
        Assert.Contains("28 significant digits", warning);
    }

    /// <summary>BigInteger narrows too, and says what it costs rather than the same sentence.</summary>
    [Fact]
    public void ABigIntegerSaysWhatItCosts() {
        var schema = Parse("""
            "count": { "target": "smithy.api#BigInteger" }
            """, out var diagnostics);

        Assert.Equal("long", TypeMapper.MapPropertyToCSharpType(Assert.Single(schema.Properties)));

        var warning = Assert.Single(diagnostics);

        Assert.Contains("'Money.count'", warning);
        Assert.Contains("64 bits", warning);
    }

    /// <summary>
    /// H-31. One warning per member, each naming its own - three members used to produce three
    /// copies of one sentence naming none. Qualified by the shape, because two structures may each
    /// hold an <c>amount</c>.
    /// </summary>
    [Fact]
    public void EveryNarrowedMemberIsNamedOnce() {
        Parse("""
            "unitPrice": { "target": "smithy.api#BigDecimal" },
            "discount":  { "target": "smithy.api#BigDecimal" },
            "total":     { "target": "smithy.api#BigDecimal" }
            """, out var diagnostics);

        Assert.Equal(3, diagnostics.Count);
        Assert.Equal(3, diagnostics.Distinct().Count());
        Assert.Contains(diagnostics, d => d.Contains("'Money.unitPrice'"));
        Assert.Contains(diagnostics, d => d.Contains("'Money.discount'"));
        Assert.Contains(diagnostics, d => d.Contains("'Money.total'"));
    }
}

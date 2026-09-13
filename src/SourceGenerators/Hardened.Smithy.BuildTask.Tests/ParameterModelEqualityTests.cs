using Hardened.Generation.Models;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// Every member of <see cref="ParameterModel"/> participates in equality, and the derived members
/// answer what the emitters read them for.
/// </summary>
/// <remarks>
/// <para>
/// This type rides Roslyn's incremental cache. A member left out of <c>Equals</c> is a member whose
/// edit produces a cached document that no longer matches the contract, and nothing fails at the
/// time: the build stays green and the document goes stale. EnumValues, the refs and the array item
/// facts were each left out once, which is why this asserts member by member rather than on a
/// couple of representative fields.
/// </para>
/// <para>
/// The mutation is keyed by name rather than passed as a delegate because <c>ParameterModel</c> is
/// internal, and a public test signature cannot name it.
/// </para>
/// </remarks>
public class ParameterModelEqualityTests
{
    private static ParameterModel Model() =>
        new()
        {
            Name = "id",
            In = "query",
            IsRequired = true,
            IsNullable = false,
            Default = "1",
            Description = "the id",
            MemberNameOverride = "identifier",
            Type = "integer",
            Format = "int32",
            Ref = "#/components/schemas/Id",
            IsArray = false,
            ArrayItemsType = "string",
            ArrayItemsRef = "#/components/schemas/Tag",
            ArrayItemsFormat = "uuid",
            MinLength = 1,
            MaxLength = 8,
            Minimum = 0m,
            Maximum = 10m,
            ExclusiveMinimum = false,
            ExclusiveMaximum = false,
            Pattern = "^a",
            RouteConstraint = null,
            MinItems = 1,
            MaxItems = 4,
            SchemaFacets = "{}",
            RequiredByConstraint = false,
            EnumValues = ["a", "b"],
        };

    public static TheoryData<string> Members =>
        new()
        {
            "Name",
            "In",
            "IsRequired",
            "IsNullable",
            "Default",
            "Description",
            "MemberNameOverride",
            "Type",
            "Format",
            "Ref",
            "IsArray",
            "ArrayItemsType",
            "ArrayItemsRef",
            "ArrayItemsFormat",
            "MinLength",
            "MaxLength",
            "Minimum",
            "Maximum",
            "ExclusiveMinimum",
            "ExclusiveMaximum",
            "Pattern",
            "RouteConstraint",
            "MinItems",
            "MaxItems",
            "SchemaFacets",
            "RequiredByConstraint",
            "EnumValues",
        };

    private static void Change(ParameterModel model, string member)
    {
        switch (member)
        {
            case "Name":
                model.Name = "other";
                break;
            case "In":
                model.In = "path";
                break;
            case "IsRequired":
                model.IsRequired = false;
                break;
            case "IsNullable":
                model.IsNullable = true;
                break;
            case "Default":
                model.Default = "2";
                break;
            case "Description":
                model.Description = "other";
                break;
            case "MemberNameOverride":
                model.MemberNameOverride = "other";
                break;
            case "Type":
                model.Type = "string";
                break;
            case "Format":
                model.Format = "int64";
                break;
            case "Ref":
                model.Ref = "#/other";
                break;
            case "IsArray":
                model.IsArray = true;
                break;
            case "ArrayItemsType":
                model.ArrayItemsType = "integer";
                break;
            case "ArrayItemsRef":
                model.ArrayItemsRef = "#/other";
                break;
            case "ArrayItemsFormat":
                model.ArrayItemsFormat = "date";
                break;
            case "MinLength":
                model.MinLength = 2;
                break;
            case "MaxLength":
                model.MaxLength = 9;
                break;
            case "Minimum":
                model.Minimum = 1m;
                break;
            case "Maximum":
                model.Maximum = 11m;
                break;
            case "ExclusiveMinimum":
                model.ExclusiveMinimum = true;
                break;
            case "ExclusiveMaximum":
                model.ExclusiveMaximum = true;
                break;
            case "Pattern":
                model.Pattern = "^b";
                break;
            case "RouteConstraint":
                model.RouteConstraint = "int";
                break;
            case "MinItems":
                model.MinItems = 2;
                break;
            case "MaxItems":
                model.MaxItems = 5;
                break;
            case "SchemaFacets":
                model.SchemaFacets = "{\"x\":1}";
                break;
            case "RequiredByConstraint":
                model.RequiredByConstraint = true;
                break;
            case "EnumValues":
                model.EnumValues = ["a", "c"];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member), member, "no case");
        }
    }

    [Theory]
    [MemberData(nameof(Members))]
    public void ChangingAnyMember_MakesTheModelUnequal(string member)
    {
        var mutated = Model();
        Change(mutated, member);

        Assert.False(Model().Equals(mutated), $"{member} is not compared by Equals");
    }

    [Fact]
    public void TwoModelsBuiltTheSameWay_AreEqual()
    {
        Assert.Equal(Model(), Model());
        Assert.Equal(Model().GetHashCode(), Model().GetHashCode());
    }

    [Fact]
    public void AModel_EqualsItself()
    {
        var model = Model();

        Assert.True(model.Equals(model));
    }

    [Fact]
    public void NullAndOtherTypes_AreNotEqual()
    {
        Assert.False(Model().Equals(null));
        Assert.False(Model().Equals((object?)"not a parameter"));
        Assert.True(Model().Equals((object?)Model()));
    }

    /// <summary>
    /// EnumValues is compared by contents, and an empty list reads the same as none at all: the
    /// spec has no way to write an empty enum, so the two spellings mean one thing.
    /// </summary>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(new string[0], null, true)]
    [InlineData(new[] { "a" }, new[] { "a" }, true)]
    [InlineData(new[] { "a" }, new[] { "b" }, false)]
    [InlineData(new[] { "a" }, new[] { "a", "b" }, false)]
    [InlineData(new[] { "a" }, null, false)]
    public void EnumValues_AreComparedByContents(string[]? left, string[]? right, bool equal)
    {
        var a = new ParameterModel { EnumValues = left?.ToList() };
        var b = new ParameterModel { EnumValues = right?.ToList() };

        Assert.Equal(equal, a.Equals(b));
    }

    /// <summary>
    /// The derived members the emitters read. Requiredness and nullability are orthogonal in
    /// OpenAPI 3.0, and each of these reads the pair differently.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    public void RequirednessAndNullability_DecideTheGeneratedShape(
        bool required,
        bool nullable,
        bool csharpNullable,
        bool hasDefault,
        bool constrainedAsRequired
    )
    {
        var model = new ParameterModel { IsRequired = required, IsNullable = nullable };

        Assert.Equal(csharpNullable, model.IsCSharpNullable);
        Assert.Equal(hasDefault, model.HasDefault);
        Assert.Equal(constrainedAsRequired, model.ConstrainedAsRequired);
    }

    [Fact]
    public void MemberName_PrefersTheOverride()
    {
        Assert.Equal(
            "identifier",
            new ParameterModel { Name = "id", MemberNameOverride = "identifier" }.MemberName
        );
    }

    /// <summary>
    /// A path parameter is present whenever the route matched, so required on one is a check that
    /// cannot fail, and a pattern already compiled into a route constraint has refused every value
    /// that would have failed it. Counting either promises a 400 nothing can answer.
    /// </summary>
    [Fact]
    public void APathParameterThatIsOnlyRequired_CarriesNoConstraint()
    {
        Assert.False(
            new ParameterModel { In = "path", IsRequired = true }.HasValidationConstraints
        );
        Assert.True(
            new ParameterModel { In = "query", IsRequired = true }.HasValidationConstraints
        );
    }

    [Fact]
    public void APatternAlreadyInTheRouteConstraint_CarriesNoConstraint()
    {
        Assert.False(
            new ParameterModel
            {
                In = "path",
                Pattern = "^a",
                RouteConstraint = "regex",
            }.HasValidationConstraints
        );
        Assert.True(new ParameterModel { In = "path", Pattern = "^a" }.HasValidationConstraints);
    }
}

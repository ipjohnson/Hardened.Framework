using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Which schema properties a generated validator descends into.
/// </summary>
/// <remarks>
/// <para>
/// The defect these were written for: <c>[ValidateNested]</c> was emitted on an operation's
/// <c>body</c> parameter and on nothing else, so a constraint on an array's items or on a nested
/// object was generated, registered in DI, and never called.
/// <c>POST /orders {"lines":[{"sku":"TLS-0001","quantity":0}]}</c> against <c>minimum: 1</c>
/// answered 201 and placed the order.
/// </para>
/// <para>
/// Only the mark was missing. <c>ValidationModules</c>' <c>[ValidateNested]</c> already validates an
/// object, each element of a collection and each value of a dictionary, indexed through
/// <c>ValidationContext.PushIndex</c>.
/// </para>
/// </remarks>
public class NestedValidationTests
{
    /// <summary>A line item with a constraint of its own, and so a validator of its own.</summary>
    private static SchemaModel ConstrainedLine() =>
        new()
        {
            Name = "OrderLine",
            Kind = SchemaKind.Object,
            Required = new List<string> { "sku", "quantity" },
            Properties = new List<PropertyModel>
            {
                new()
                {
                    Name = "sku",
                    Type = "string",
                    IsRequired = true,
                },
                new()
                {
                    Name = "quantity",
                    Type = "integer",
                    IsRequired = true,
                    Minimum = 1,
                },
            },
        };

    /// <summary>The same shape with nothing to check, so no validator is generated for it.</summary>
    private static SchemaModel UnconstrainedLine() =>
        new()
        {
            Name = "OrderLine",
            Kind = SchemaKind.Object,
            Properties = new List<PropertyModel>
            {
                new() { Name = "sku", Type = "string" },
                new() { Name = "note", Type = "string" },
            },
        };

    private static SchemaModel OrderWith(PropertyModel lines) =>
        new()
        {
            Name = "Order",
            Kind = SchemaKind.Object,
            Required = new List<string> { "lines" },
            Properties = new List<PropertyModel> { lines },
        };

    private static PropertyModel ArrayOfLines() =>
        new()
        {
            Name = "lines",
            IsArray = true,
            ArrayItemsRef = "#/components/schemas/OrderLine",
            IsRequired = true,
        };

    /// <summary>A model with a constraint of its own: a required, bounded string.</summary>
    private static SchemaModel Receipt() =>
        new()
        {
            Name = "Receipt",
            Kind = SchemaKind.Object,
            Required = new List<string> { "payload" },
            Properties = new List<PropertyModel>
            {
                new()
                {
                    Name = "payload",
                    Type = "string",
                    IsRequired = true,
                    MaxLength = 16,
                },
            },
        };

    /// <summary>
    /// A model with no constraint of its own: one optional member referring to another model.
    /// </summary>
    private static SchemaModel Holding(string name, string member, string target) =>
        new()
        {
            Name = name,
            Kind = SchemaKind.Object,
            Properties = new List<PropertyModel>
            {
                new() { Name = member, Ref = "#/components/schemas/" + target },
            },
        };

    /// <summary>
    /// An array whose items carry constraints is descended into. The exact D2 repro.
    /// </summary>
    [Fact]
    public void AnArrayOfConstrainedObjectsIsDescendedInto()
    {
        var result = EmitterHarness.Schema(OrderWith(ArrayOfLines()), [ConstrainedLine()]);

        Assert.Contains("ValidateNested", result);
    }

    /// <summary>
    /// A nested object carrying constraints is descended into, the same as an array's items.
    /// </summary>
    [Fact]
    public void ANestedConstrainedObjectIsDescendedInto()
    {
        var property = new PropertyModel
        {
            Name = "line",
            Ref = "#/components/schemas/OrderLine",
            IsRequired = true,
        };

        Assert.Contains(
            "ValidateNested",
            EmitterHarness.Schema(OrderWith(property), [ConstrainedLine()])
        );
    }

    /// <summary>
    /// A dictionary of constrained objects too - ValidateNested validates each value, pathed as
    /// <c>map[key]</c>.
    /// </summary>
    [Fact]
    public void ADictionaryOfConstrainedObjectsIsDescendedInto()
    {
        var property = new PropertyModel
        {
            Name = "lines",
            IsDictionary = true,
            DictionaryValueRef = "#/components/schemas/OrderLine",
            IsRequired = true,
        };

        Assert.Contains(
            "ValidateNested",
            EmitterHarness.Schema(OrderWith(property), [ConstrainedLine()])
        );
    }

    /// <summary>
    /// Not marked when the nested type has nothing to check.
    /// </summary>
    /// <remarks>
    /// The guard that matters. <c>[ValidateNested]</c> naming a validator the validation generator
    /// declined to emit is <c>CS0234</c> in a generated file - a build failure in code the author
    /// cannot open, from a specification that is not wrong.
    /// </remarks>
    [Fact]
    public void AnArrayOfUnconstrainedObjectsIsNotDescendedInto()
    {
        var result = EmitterHarness.Schema(OrderWith(ArrayOfLines()), [UnconstrainedLine()]);

        Assert.DoesNotContain("ValidateNested", result);
    }

    /// <summary>
    /// Not marked when the element type could not be named. It degrades to <c>JsonElement</c>,
    /// which has no validator and no members to check.
    /// </summary>
    [Fact]
    public void AnArrayOfPrimitivesIsNotDescendedInto()
    {
        var property = new PropertyModel
        {
            Name = "tags",
            IsArray = true,
            ArrayItemsType = "string",
            IsRequired = true,
        };

        Assert.DoesNotContain(
            "ValidateNested",
            EmitterHarness.Schema(OrderWith(property), [ConstrainedLine()])
        );
    }

    /// <summary>
    /// Not marked when the ref resolves to nothing. A misspelled <c>$ref</c> is already reported
    /// elsewhere; what it must not do is name a validator that was never emitted.
    /// </summary>
    [Fact]
    public void AnUnresolvableRefIsNotDescendedInto()
    {
        var unrelated = UnconstrainedLine();
        unrelated.Name = "SomethingElse";

        Assert.DoesNotContain(
            "ValidateNested",
            EmitterHarness.Schema(OrderWith(ArrayOfLines()), [unrelated])
        );
    }

    /// <summary>
    /// A <c>readOnly</c> property is not descended into. It is emitted outside the constructor and
    /// carries no constraints at all - <c>required</c> on one means "always present in a response",
    /// and validation runs on request binding.
    /// </summary>
    [Fact]
    public void AReadOnlyNestedObjectIsNotDescendedInto()
    {
        var property = new PropertyModel
        {
            Name = "line",
            Ref = "#/components/schemas/OrderLine",
            IsReadOnly = true,
        };

        Assert.DoesNotContain(
            "ValidateNested",
            EmitterHarness.Schema(OrderWith(property), [ConstrainedLine()])
        );
    }

    /// <summary>
    /// A nested model's own nested members are marked when that schema is emitted, by the same
    /// rule.
    /// </summary>
    [Fact]
    public void DescentComposesOneLevelAtATime()
    {
        var line = ConstrainedLine();

        var basket = new SchemaModel
        {
            Name = "Basket",
            Kind = SchemaKind.Object,
            Required = new List<string> { "order" },
            Properties = new List<PropertyModel>
            {
                new()
                {
                    Name = "order",
                    Ref = "#/components/schemas/Order",
                    IsRequired = true,
                },
            },
        };

        var order = OrderWith(ArrayOfLines());
        var all = new List<SchemaModel> { basket, order, line };

        Assert.Contains("ValidateNested", EmitterHarness.Schema(basket, all));
        Assert.Contains("ValidateNested", EmitterHarness.Schema(order, all));
    }

    /// <summary>
    /// A member whose model has no constraint of its own, only a nested model that does, is
    /// descended into. The #494 repro: <c>Outer.middle</c> went unmarked, so <c>OuterValidator</c>
    /// never reached <c>Middle.receipt.payload</c>.
    /// </summary>
    [Fact]
    public void AMemberWhoseModelOnlyNestsConstraintsIsDescendedInto()
    {
        var receipt = Receipt();
        var middle = Holding("Middle", "receipt", "Receipt");
        var outer = Holding("Outer", "middle", "Middle");

        List<SchemaModel> all = [outer, middle, receipt];

        Assert.Contains("ValidateNested", EmitterHarness.Schema(outer, all));
        Assert.Contains("ValidateNested", EmitterHarness.Schema(middle, all));
    }

    /// <summary>
    /// The walk stops at the model with nothing to check, however deep it is.
    /// </summary>
    [Fact]
    public void AMemberWhoseModelsNestNothingToCheckIsNotDescendedInto()
    {
        var middle = Holding("Middle", "line", "OrderLine");
        var outer = Holding("Outer", "middle", "Middle");

        Assert.DoesNotContain(
            "ValidateNested",
            EmitterHarness.Schema(outer, [outer, middle, UnconstrainedLine()])
        );
    }

    /// <summary>
    /// Two schemas that refer to each other are marked when a constraint is reachable, and the walk
    /// ends. Each side's answer depends on the other's, so a walk without a guard never returns.
    /// </summary>
    [Fact]
    public void SchemasReferringToEachOtherAreMarkedWhenAConstraintIsReachable()
    {
        var a = Holding("A", "b", "B");
        var b = Holding("B", "a", "A");

        b.Properties.Add(
            new PropertyModel
            {
                Name = "code",
                Type = "string",
                MaxLength = 3,
            }
        );

        List<SchemaModel> all = [a, b];

        Assert.Contains("ValidateNested", EmitterHarness.Schema(a, all));
        Assert.Contains("ValidateNested", EmitterHarness.Schema(b, all));
    }

    /// <summary>
    /// A cycle with nothing to check anywhere on it is not marked. Neither side gets a validator.
    /// </summary>
    [Fact]
    public void SchemasReferringToEachOtherWithNothingToCheckAreNotMarked()
    {
        var a = Holding("A", "b", "B");
        var b = Holding("B", "a", "A");

        List<SchemaModel> all = [a, b];

        Assert.DoesNotContain("ValidateNested", EmitterHarness.Schema(a, all));
        Assert.DoesNotContain("ValidateNested", EmitterHarness.Schema(b, all));
    }

    [Fact]
    public void ASelfReferencingSchemaWithNothingToCheckIsNotMarked()
    {
        var node = Holding("Node", "next", "Node");

        Assert.DoesNotContain("ValidateNested", EmitterHarness.Schema(node, [node]));
    }

    /// <summary>
    /// A self-referencing schema with a constraint of its own is marked, and the build does not
    /// hang.
    /// </summary>
    [Fact]
    public void ASelfReferencingSchemaIsMarked()
    {
        var node = new SchemaModel
        {
            Name = "Node",
            Kind = SchemaKind.Object,
            Required = new List<string> { "name" },
            Properties = new List<PropertyModel>
            {
                new()
                {
                    Name = "name",
                    Type = "string",
                    IsRequired = true,
                    MinLength = 1,
                },
                new()
                {
                    Name = "children",
                    IsArray = true,
                    ArrayItemsRef = "#/components/schemas/Node",
                },
            },
        };

        Assert.Contains("ValidateNested", EmitterHarness.Schema(node, [node]));
    }
}

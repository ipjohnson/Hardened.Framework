using System.Reflection;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// <c>Response&lt;T1..T8&gt;</c>, and the shape every generator downstream of it assumes.
///
/// <para>
/// Hardened matches a response set structurally - one public single-parameter constructor per case
/// plus a public <c>object? Value</c> - rather than by the <c>[Union]</c> attribute or the C# 15
/// keyword. That is what lets this struct, a generated response union and a language union travel
/// one code path, and it is why the generators need no toolchain awareness. It is also entirely
/// implicit: nothing in the compiler enforces it, so a second field or an extra constructor added
/// here would break the match with no error at the point of the edit. The sweep below is what makes
/// that edit fail.
/// </para>
/// </summary>
public class ResponseStructTests {

    /// <summary>
    /// Every arity, found by reflection rather than listed, so a ninth added later is covered
    /// without anyone remembering to add it.
    /// </summary>
    public static TheoryData<Type> Arities {
        get {
            var data = new TheoryData<Type>();

            foreach (var type in typeof(Response<,>).Assembly.GetExportedTypes()) {
                if (type.IsGenericTypeDefinition &&
                    type.Name.StartsWith("Response`", StringComparison.Ordinal)) {
                    data.Add(type);
                }
            }

            return data;
        }
    }

    #region the structural contract

    [Fact]
    public void EveryArityFromTwoToEight_Exists() {
        var arities = typeof(Response<,>).Assembly.GetExportedTypes()
            .Where(t => t.IsGenericTypeDefinition &&
                        t.Name.StartsWith("Response`", StringComparison.Ordinal))
            .Select(t => t.GetGenericArguments().Length)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8 }, arities);
    }

    /// <summary>
    /// A struct, because naming by inheritance was the only argument for a class and it does not
    /// work - a derived type has no conversion to its base's operators and fails CS0266. Readonly
    /// because a response set is a value that has already been decided.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryArity_IsAReadonlyStruct(Type type) {
        Assert.True(type.IsValueType, type.Name + " must be a struct.");

        Assert.True(
            type.GetCustomAttributes()
                .Any(a => a.GetType().Name == "IsReadOnlyAttribute"),
            type.Name + " must be a readonly struct.");
    }

    /// <summary>
    /// Public, and typed <c>object?</c> rather than a generic - it is what the structural rule
    /// names, and what the generated dispatch switches on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryArity_ExposesAPublicObjectValue(Type type) {
        var value = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(value);
        Assert.Equal(typeof(object), value!.PropertyType);
        Assert.True(value.CanRead);
        Assert.False(value.CanWrite);
    }

    /// <summary>
    /// One per case and nothing else. An extra constructor - a convenience taking two values, a
    /// copy constructor - would be read by the structural matcher as another case.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryArity_HasExactlyOneSingleParameterConstructorPerCase(Type type) {
        var arity = type.GetGenericArguments().Length;

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(arity, constructors.Length);
        Assert.All(constructors, c => Assert.Single(c.GetParameters()));

        // Each takes a distinct type parameter, in order. Two constructors over the same one would
        // be the CS0457 shape the per-status wrapper types exist to avoid.
        var taken = constructors.Select(c => c.GetParameters()[0].ParameterType).ToList();

        Assert.Equal(arity, taken.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryArity_HasOneImplicitConversionPerCase(Type type) {
        var arity = type.GetGenericArguments().Length;

        var conversions = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Implicit")
            .ToList();

        Assert.Equal(arity, conversions.Count);
        Assert.All(conversions, m => Assert.Equal(type, m.ReturnType.GetGenericTypeDefinition()));
    }

    /// <summary>
    /// Nothing but <c>Value</c>. A second field would be state the structural matcher cannot see
    /// and the generated dispatch would never read.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryArity_CarriesOneFieldOnly(Type type) {
        var fields = type.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Single(fields);
    }

    #endregion

    #region behaviour

    /// <summary>
    /// The reason a handler can return the bare value rather than wrapping it, which is what makes
    /// the signature readable at the call site.
    /// </summary>
    [Fact]
    public void ImplicitConversion_AcceptsEachCase() {
        Response<string, NotFound> fromFirst = "todo";
        Response<string, NotFound> fromSecond = new NotFound("todo");

        Assert.Equal("todo", fromFirst.Value);
        Assert.IsType<NotFound>(fromSecond.Value);
    }

    /// <summary>
    /// Eight is the cap, so it is the arity most likely to be wrong and least likely to be used
    /// while it is.
    /// </summary>
    [Fact]
    public void ImplicitConversion_WorksAtTheHighestArity() {
        Response<string, int, bool, Guid, TimeSpan, Uri, NotFound, Conflict> last =
            new Conflict("clash");

        Assert.IsType<Conflict>(last.Value);
    }

    [Fact]
    public void Constructor_HoldsTheCaseItWasGiven() {
        var response = new Response<string, NotFound>(new NotFound("todo"));

        Assert.IsType<NotFound>(response.Value);
    }

    /// <summary>
    /// <c>return default;</c> compiles and bypasses every constructor, so the null is reachable
    /// from user code rather than merely theoretical. The generated dispatch has to answer it, and
    /// this is the test that says it can occur.
    /// </summary>
    [Fact]
    public void Default_HasNoCase() {
        Response<string, NotFound> uninitialised = default;

        Assert.Null(uninitialised.Value);
    }

    /// <summary>
    /// A target-typed switch over the arms is how a handler picks a case, so it has to bind through
    /// the conversion rather than needing an explicit construction at each arm.
    /// </summary>
    [Theory]
    [InlineData(true, "found")]
    [InlineData(false, null)]
    public void TargetTypedSwitch_BindsThroughTheConversion(bool hit, string? expected) {
        Response<string, NotFound> result = hit ? "found" : new NotFound("todo");

        Assert.Equal(expected, result.Value as string);
    }

    [Fact]
    public void ToString_RendersTheCaseRatherThanTheWrapper() {
        Response<string, NotFound> response = "todo";

        Assert.Equal("todo", response.ToString());
    }

    [Fact]
    public void ToString_IsEmptyForADefault() {
        Response<string, NotFound> uninitialised = default;

        Assert.Equal("", uninitialised.ToString());
    }

    #endregion
}

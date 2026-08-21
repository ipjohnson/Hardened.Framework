using System.Reflection;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// Every constructor and every conversion of every arity, invoked.
/// </summary>
/// <remarks>
/// <para>
/// <c>ResponseStructTests</c> asserts the shape by reflection - that each arity declares one
/// constructor and one conversion per case. Declaring them is not the same as their working, and a
/// shape assertion never runs one: the seven arities together carry eighty-odd members, and
/// exercising two of them by hand left the rest unexecuted. That is a real gap rather than a
/// coverage number, because these are written out seven times and a transcription error in the
/// sixth would look exactly like the fifth.
/// </para>
/// <para>
/// Driven by reflection rather than written out, for the same reason the type is: writing the cases
/// by hand would repeat the error it is meant to catch.
/// </para>
/// </remarks>
public class ResponseArityTests {

    /// <summary>
    /// A distinct type per position, so a conversion that reached the wrong constructor would put
    /// the wrong value in <c>Value</c> rather than an indistinguishable one.
    /// </summary>
    private static readonly Type[] CaseTypes = [
        typeof(string), typeof(int), typeof(bool), typeof(Guid),
        typeof(TimeSpan), typeof(Uri), typeof(NotFound), typeof(Conflict)
    ];

    private static readonly object[] CaseValues = [
        "case-one", 2, true, Guid.NewGuid(),
        TimeSpan.FromSeconds(5), new Uri("https://example.test"),
        new NotFound("todo"), new Conflict("clash")
    ];

    public static TheoryData<int> Arities {
        get {
            var data = new TheoryData<int>();

            for (var arity = 2; arity <= 8; arity++) {
                data.Add(arity);
            }

            return data;
        }
    }

    private static Type Closed(int arity) =>
        typeof(Response<,>).Assembly
            .GetExportedTypes()
            .Single(t => t.IsGenericTypeDefinition &&
                         t.Name == "Response`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .MakeGenericType(CaseTypes.Take(arity).ToArray());

    /// <summary>
    /// Each constructor puts its own argument in <c>Value</c> - not the first one, and not a
    /// default.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryConstructorHoldsTheCaseItWasGiven(int arity) {
        var closed = Closed(arity);

        for (var position = 0; position < arity; position++) {
            var constructor = closed.GetConstructors()
                .Single(c => c.GetParameters()[0].ParameterType == CaseTypes[position]);

            var response = constructor.Invoke([CaseValues[position]]);

            Assert.Equal(
                CaseValues[position],
                closed.GetProperty("Value")!.GetValue(response));
        }
    }

    /// <summary>
    /// And each implicit conversion reaches the constructor for its own case. A conversion wired to
    /// the wrong one compiles, and only shows up as a response answering another case's status.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void EveryConversionReachesItsOwnCase(int arity) {
        var closed = Closed(arity);

        for (var position = 0; position < arity; position++) {
            var conversion = closed
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "op_Implicit" &&
                             m.GetParameters()[0].ParameterType == CaseTypes[position]);

            var response = conversion.Invoke(null, [CaseValues[position]]);

            Assert.Equal(
                CaseValues[position],
                closed.GetProperty("Value")!.GetValue(response));
        }
    }

    /// <summary>
    /// <c>ToString</c> renders the case rather than the wrapper, at every arity - so logging a
    /// response does not print a type name.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void ToStringRendersTheCaseAtEveryArity(int arity) {
        var closed = Closed(arity);

        for (var position = 0; position < arity; position++) {
            var constructor = closed.GetConstructors()
                .Single(c => c.GetParameters()[0].ParameterType == CaseTypes[position]);

            var response = constructor.Invoke([CaseValues[position]]);

            Assert.Equal(CaseValues[position].ToString(), response!.ToString());
        }
    }

    /// <summary>
    /// <c>default</c> is reachable at every arity, and reads as no case rather than throwing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Arities))]
    public void DefaultHasNoCaseAtEveryArity(int arity) {
        var closed = Closed(arity);
        var uninitialised = Activator.CreateInstance(closed);

        Assert.Null(closed.GetProperty("Value")!.GetValue(uninitialised));
        Assert.Equal("", uninitialised!.ToString());
    }
}

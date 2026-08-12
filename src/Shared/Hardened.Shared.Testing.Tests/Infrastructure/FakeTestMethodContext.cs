using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;

namespace Hardened.Shared.Testing.Tests.Infrastructure;

/// <summary>
/// The smallest thing the harness will accept where xUnit would hand it a test method.
/// </summary>
/// <remarks>
/// Standing this up by hand is the point. The alternative — asserting on <c>[HardenedTest]</c>
/// parameter injection from inside a <c>[HardenedTest]</c> — cannot distinguish "injection worked"
/// from "the test never ran the way I thought it did", because the same machinery both supplies the
/// arguments and decides whether the assertion is reached at all. Driving the mechanism with a
/// context built here means the assertion is on the outside of it.
/// </remarks>
internal sealed class FakeTestMethodContext : ITestMethodContext {
    private FakeTestMethodContext(MethodInfo method, IReadOnlyList<Attribute> attributes) {
        Method = method;
        Attributes = attributes;
    }

    /// <summary>
    /// A context over the named method of <typeparamref name="T"/>, with the attributes xUnit would
    /// have collected: assembly first, then the declaring type, then the method.
    /// </summary>
    public static FakeTestMethodContext For<T>(string methodName) {
        var method = typeof(T).GetMethod(methodName,
                         BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.Public | BindingFlags.NonPublic) ??
                     throw new InvalidOperationException(
                         $"No method named '{methodName}' on {typeof(T).FullName}");

        var attributes = new List<Attribute>();
        attributes.AddRange(typeof(T).Assembly.GetCustomAttributes());
        attributes.AddRange(typeof(T).GetCustomAttributes());
        attributes.AddRange(method.GetCustomAttributes());

        return new FakeTestMethodContext(method, attributes);
    }

    public MethodInfo Method { get; }

    public IReadOnlyList<Attribute> Attributes { get; }
}

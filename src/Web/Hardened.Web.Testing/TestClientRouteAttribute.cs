namespace Hardened.Web.Testing;

/// <summary>
/// Names an <see cref="ITestClientRoute"/> the assembly's tests build their clients through.
/// </summary>
/// <remarks>
/// A client testing package ships a derived attribute so the opt-in reads as the package rather
/// than as plumbing - <c>[assembly: KiotaTesting]</c> instead of
/// <c>[assembly: TestClientRoute(typeof(KiotaClientRoute))]</c> - and both forms are read the same
/// way. More than one is allowed: a solution with a Kiota client and a Refit interface declares
/// both, and each route answers for the clients it recognises.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class TestClientRouteAttribute(Type routeType) : Attribute {

    /// <summary>
    /// The route: a public, concrete <see cref="ITestClientRoute"/> with a parameterless
    /// constructor.
    /// </summary>
    public Type RouteType { get; } = routeType;
}

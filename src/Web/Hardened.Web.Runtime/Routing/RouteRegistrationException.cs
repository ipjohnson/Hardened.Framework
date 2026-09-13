namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Every route that failed to register, reported at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>At once, deliberately.</b> A registration callback runs a loop, and the failures it produces
/// are usually all the same mistake. Throwing on the first would mean starting the application once
/// per bad route to find out how many there are.
/// </para>
/// <para>
/// A route that is wrong is a build failure everywhere else in this framework. These are the checks
/// that cannot be made at build time because the path does not exist until run time, so they are
/// made as early as they can be - before the first request, rather than as a 404 in production.
/// </para>
/// </remarks>
public class RouteRegistrationException : Exception
{
    public RouteRegistrationException(IReadOnlyList<string> failures)
        : base(Describe(failures))
    {
        Failures = failures;
    }

    public IReadOnlyList<string> Failures { get; }

    private static string Describe(IReadOnlyList<string> failures)
    {
        var text = new System.Text.StringBuilder();

        text.Append(
            failures.Count == 1
                ? "A route could not be registered:"
                : $"{failures.Count} routes could not be registered:"
        );

        foreach (var failure in failures)
        {
            text.Append("\n  - ").Append(failure);
        }

        return text.ToString();
    }
}

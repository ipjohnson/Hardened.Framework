using System.Reflection;
using Xunit;

namespace Hardened.Amz.Function.Sqs.Testing.Tests;

/// <summary>
/// Scaffolding, created 2026-08-11 as part of Phase 0 of TESTING-PLAN.md.
///
/// <para>
/// This project exists so the <c>aws-clients-cdk</c> workstream can add test files without editing the
/// solution file — which is what keeps the workstreams from conflicting with each other. The one
/// test below asserts the project references resolve at run time, and nothing more.
/// </para>
///
/// <para>
/// <b>Delete this file</b> once real tests land. It is a placeholder, not coverage.
/// </para>
/// </summary>
public class ScaffoldTests {

    [Fact]
    public void TheAssemblyUnderTestIsReferencedAndLoadable() {
        var assembly = Assembly.Load("Hardened.Amz.Function.Sqs.Testing");

        Assert.NotEmpty(assembly.GetExportedTypes());
    }
}

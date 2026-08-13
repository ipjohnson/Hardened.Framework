using System.Text;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// Runs every scenario once through every pipeline and reports the status and body.
///
/// This exists because the failure mode of a pipeline benchmark is silent. A route that does not
/// match returns 404 from the terminal delegate, writes nothing, and completes far faster than a
/// route that does match — so a misconfigured harness does not error, it just reports an
/// impressive number for doing nothing. Nothing here is measured; it only establishes that the
/// pipelines are doing the same work before any timing is believed.
///
/// It also cross-checks the response bodies against each other. Identical routing with a
/// different payload — a field dropped by a serializer setting, a model bound to its default —
/// would otherwise pass a status check while making the comparison meaningless.
///
/// <c>Program</c> runs this automatically before benchmarking; <c>--verify</c> runs it alone.
/// </summary>
public static class PipelineVerification {

    public static bool Run(bool includeAspNet, TextWriter output) {
        var harnesses = new List<IPipelineHarness> {
            new HardenedNativeHarness(),
            new HardenedFeatureHarness(),
            new HardenedAspNetHarness()
        };

        if (includeAspNet) {
            harnesses.Add(new AspNetHarness(AspNetFlavor.MinimalApi, sourceGeneratedJson: false));
            harnesses.Add(new AspNetHarness(AspNetFlavor.Mvc, sourceGeneratedJson: false));
        }

        try {
            return Verify(harnesses, output);
        }
        finally {
            foreach (var harness in harnesses) {
                harness.Dispose();
            }
        }
    }

    private static bool Verify(List<IPipelineHarness> harnesses, TextWriter output) {
        var passed = true;

        output.WriteLine();
        output.WriteLine("Pipeline verification");
        output.WriteLine(new string('-', 100));
        output.WriteLine($"{"pipeline",-22} {"scenario",-26} {"status",-7} body");
        output.WriteLine(new string('-', 100));

        foreach (var scenario in Scenarios.Verification) {
            var bodies = new Dictionary<string, string>();

            foreach (var harness in harnesses) {
                var responseBody = new MemoryStream();
                var status = harness.Execute(scenario, responseBody).GetAwaiter().GetResult();
                var body = Encoding.UTF8.GetString(responseBody.ToArray());

                // A successful route has to produce a payload; a miss is expected to produce
                // nothing, so only the status is meaningful there.
                var ok = status == scenario.ExpectedStatus &&
                    (scenario.ExpectedStatus != 200 || body.Length > 0);

                passed &= ok;

                if (scenario.ExpectedStatus == 200) {
                    bodies[harness.Name] = body;
                }

                var shown = body.Length > 46 ? body[..46] + "..." : body;

                output.WriteLine($"{harness.Name,-22} {scenario.Name,-26} {status,-7} " +
                    (ok
                        ? shown
                        : $"<< expected {scenario.ExpectedStatus}, got {status} " +
                          $"{(body.Length == 0 ? "and no body" : shown)} >>"));
            }

            passed &= ReportBodyMismatches(scenario, bodies, output);
        }

        output.WriteLine(new string('-', 100));
        output.WriteLine(passed
            ? "OK - every pipeline returned the expected status, and all agreed on the response body."
            : "FAILED - the pipelines are not doing equivalent work. Timings would be meaningless.");
        output.WriteLine();

        return passed;
    }

    /// <summary>
    /// Compares bodies after normalizing property-name case. The frameworks are free to differ on
    /// casing — that is a serializer setting, not a difference in work done — but a difference in
    /// the actual values means one of them bound or computed something the others did not.
    /// </summary>
    private static bool ReportBodyMismatches(
        RequestScenario scenario, Dictionary<string, string> bodies, TextWriter output) {
        if (bodies.Count < 2) {
            return true;
        }

        var reference = bodies.First();
        var mismatches = bodies
            .Where(entry => !string.Equals(entry.Value, reference.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mismatches.Count == 0) {
            return true;
        }

        output.WriteLine($"  ! body mismatch on {scenario.Name}");
        output.WriteLine($"      {reference.Key}: {reference.Value}");

        foreach (var mismatch in mismatches) {
            output.WriteLine($"      {mismatch.Key}: {mismatch.Value}");
        }

        return false;
    }
}

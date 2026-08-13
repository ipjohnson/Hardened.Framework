using BenchmarkDotNet.Running;
using Hardened.Benchmarks.Infrastructure;

// Hardened's own benchmarks run by default. The ASP.NET Core comparison is opt-in via --aspnet,
// because "did Hardened regress" is the question asked routinely and "how does Hardened compare
// to MVC" is the one asked deliberately.
//
// Anything not consumed here is handed to BenchmarkDotNet untouched, so the usual switches still
// work: --job short for a quick pass, --filter to narrow, --runtimes to add a target.

var includeAspNet = args.Contains("--aspnet");
var verifyOnly = args.Contains("--verify");
var skipVerify = args.Contains("--no-verify");

if (!skipVerify) {
    // Always run before benchmarking. A pipeline that fails to route still completes, quickly
    // and quietly, so an unverified run can report a fast number for producing a 404.
    if (!PipelineVerification.Run(includeAspNet, Console.Out)) {
        return 1;
    }
}

if (verifyOnly) {
    return 0;
}

var categories = includeAspNet
    ? [.. BenchmarkCategories.DefaultCategories, BenchmarkCategories.AspNet]
    : BenchmarkCategories.DefaultCategories;

var benchmarkArgs = args
    .Where(argument => argument is not ("--aspnet" or "--verify" or "--no-verify"))
    .ToList();

// Without an explicit filter BenchmarkSwitcher drops into an interactive prompt, which is not
// what "run the benchmarks" should do.
if (!benchmarkArgs.Any(argument => argument.StartsWith("--filter", StringComparison.Ordinal))) {
    benchmarkArgs.Add("--filter");
    benchmarkArgs.Add("*");
}

if (!benchmarkArgs.Any(argument => argument.StartsWith("--anyCategories", StringComparison.Ordinal))) {
    benchmarkArgs.Add("--anyCategories");
    benchmarkArgs.AddRange(categories);
}

BenchmarkSwitcher
    .FromAssembly(typeof(BenchmarkCategories).Assembly)
    .Run([.. benchmarkArgs]);

return 0;

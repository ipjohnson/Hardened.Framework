namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// One request pipeline, driven from a <see cref="RequestScenario"/> and writing to a caller
/// supplied stream.
///
/// All four implementations do the same amount of framing work behind this method: create a DI
/// scope, build whatever context object the pipeline requires, run it, and leave the serialized
/// response in <c>responseBody</c>. Keeping that boundary identical is what makes the timings
/// comparable — if one harness built its context in setup and another built it per request, the
/// difference between them would be an artifact of the harness rather than of the framework.
/// </summary>
public interface IPipelineHarness : IDisposable {
    string Name { get; }

    Task<int> Execute(RequestScenario scenario, MemoryStream responseBody);
}

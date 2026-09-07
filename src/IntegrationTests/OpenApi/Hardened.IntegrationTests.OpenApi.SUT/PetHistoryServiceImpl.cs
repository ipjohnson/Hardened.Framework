using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// Implements the interface <c>events.yaml</c> generates: an operation whose success is a stream,
/// declared with <c>itemSchema</c>, which the generated signature turns into
/// <c>IAsyncEnumerable&lt;PetEvent&gt;</c>.
/// </summary>
/// <remarks>
/// The refusal is thrown before the first event, which is the only place a stream can refuse:
/// once an event has been written the status is gone. That is the same shape a code-first
/// <c>[ServerSentEvents]</c> handler takes with <c>[Throws&lt;NotFound&gt;]</c>.
/// </remarks>
[Handler]
public class PetHistoryServiceImpl : IPetHistoryService {

    public async IAsyncEnumerable<PetEvent> PetEvents(string petId) {
        if (petId == "missing") {
            throw new NotFound("pet", $"No pet has id {petId}.").AsException();
        }

        await Task.Yield();

        yield return new PetEvent(petId, "adopted");
        yield return new PetEvent(petId, "weighed");
    }
}

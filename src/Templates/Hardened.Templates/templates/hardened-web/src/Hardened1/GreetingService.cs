using Hardened.Requests.Abstract.Attributes;
using Hardened1.Models;
using Hardened1.Services;

namespace Hardened1;

/// <summary>
/// The implementation of a service interface the build wrote from contracts/greeting.yaml.
/// </summary>
/// <remarks>
/// There are no route attributes anywhere in this project - the verb and the path came from the
/// document, and the generated routing table points here. [Handler] is what marks this class as
/// the implementation to route to.
///
/// IGreetingService and Greeting do not exist until the first build. Add an operation to the
/// document and this class stops satisfying the interface, which is the point: the specification
/// and the code cannot drift apart without the build saying so.
///
/// Run a build, then read obj/Debug/net8.0/openapi/generated/ to see the interface, the models,
/// the routing table and the validation the constraints in the document produced.
/// </remarks>
[Handler]
public class GreetingService : IGreetingService {

#if (openapi)
    public Task<Greeting> Hello(string name) =>
        Task.FromResult(new Greeting($"Hello, {name}!"));
#endif
#if (smithy)
    public Task<HelloOutput> Hello(string name) =>
        Task.FromResult(new HelloOutput($"Hello, {name}!"));
#endif
}

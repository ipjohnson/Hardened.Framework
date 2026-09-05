#if (kestrel)
using Hardened.Web.Kestrel.Runtime;
#endif
#if (aspnet)
using Hardened.Web.AspNetCore.Runtime;
#endif

namespace Hardened1.Tests;

/// <summary>
/// One test on a real socket. Everything else in this project runs the pipeline in-process,
/// which is fast and sees everything but the host; this one runs the application on the host
/// it deploys to, on a loopback port the kernel picks, and reads what the server itself wrote.
/// </summary>
/// <remarks>
/// The attribute is the one the application names its host with, and Bootstrap.cs is what makes
/// it mean "run here" on a test. It goes on this class and not on the assembly, because every
/// test carrying it binds and stops a server of its own. Everything a test holds is the same
/// here: a client parameter sends to the socket, a [Mock] behind a route is the same substitute,
/// and ITestWebApp sends to the socket too, for a request the client cannot make.
/// </remarks>
#if (kestrel)
[KestrelRuntime]
#endif
#if (aspnet)
[AspNetCoreRuntime]
#endif
public class TemplateModuleNameSocketTests {
#if (kiotaClient)

    [HardenedTest]
    public async Task ListTodos_OverTheSocket(TemplateModuleNameClient client) {
        var todos = await client.Todos.GetAsync().Returns<Ok<List<ClientModels.Todo>>>();

        Assert.Equal([1, 2], todos.Value.Select(todo => todo.Id!.Value));
        Assert.True(todos.Headers!.ContainsKey("Date"), "a header only a server writes");
    }
#endif
#if (refitClient)

    [HardenedTest]
    public async Task ListTodos_OverTheSocket(ITemplateModuleNameClient client) {
#if (codeFirst)
        var todos = await client.All().Returns<Ok<ICollection<ClientModels.Todo>>>();
#else
        var todos = await client.ListTodos().Returns<Ok<ICollection<ClientModels.Todo>>>();
#endif

        Assert.Equal([1, 2], todos.Value.Select(todo => todo.Id));
        Assert.True(todos.Headers!.ContainsKey("Date"), "a header only a server writes");
    }
#endif
#if (!hasClient)

    [HardenedTest]
    public async Task ListTodos_OverTheSocket(ITestWebApp app) {
        var response = await app.Get("/todos");

        response.Assert.Ok();
        Assert.True(response.Headers.ContainsKey("Date"), "a header only a server writes");
    }
#endif
}

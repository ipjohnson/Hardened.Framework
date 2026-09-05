# Credentials

Who a request is sent as is an attribute. Three parameters of one client type with three
attributes are three callers:

```csharp
[HardenedTest]
public async Task ReadingPetsNeedsTheGrant(
    [Grants("pets:read")] PetsClient reader,
    [Anonymous] PetsClient nobody,
    [Grants("pets:write")] PetsClient writer) {

    await reader.Pets.GetAsync().Returns<Ok<List<ClientModels.Pet>>>();
    await nobody.Pets.GetAsync().ReturnsStatus<Unauthorized>();
    await writer.Pets.GetAsync().ReturnsStatus<Forbidden>();
}
```

The credential applies to the `HttpClient` under each client, to `app.Get` and its siblings, and
to every request on a [socket host](/guide/testing-hosts).

## The attributes

| Attribute | Sends |
|---|---|
| `[Grants("todos:read", "todos:write")]` | `X-Test-Grants` naming the grants |
| `[Subject("pia")]` | `X-Test-Subject` naming the caller. A subject with no grants is an authenticated caller holding nothing |
| `[Anonymous]` | Nothing, and cancels whatever a wider level declared |

`[Grants]` and `[Subject]` combine. A method carrying both sends both.

## Where they go

The attributes are valid on a parameter, a method, a class and the assembly. The narrowest wins:

| Level | Beats |
|---|---|
| parameter | method, class, assembly |
| method | class, assembly |
| class | assembly |

A `[Grants]` on a parameter applies over an `[Anonymous]` method. An `[Anonymous]` on a parameter
cancels everything wider. A parameter with no attribute takes the method's credential.

## What reads the headers

`TestGrantsPrincipalSource`, from `Hardened.Requests.Testing`, is registered by `[WebTesting]`
beside the application's own principal sources. It answers only a request carrying the headers,
so a test with no attributes exercises the application's own authentication untouched. Outside a
test project nothing registers it, and the headers mean nothing.

The principal it builds carries the grants and the subject, so `[AuthorizeGrants]`, a policy and
`ICurrentCaller` all see the caller the test named. See [Authorization](/guide/authorization).

## Through ITestWebApp

`app.Get` and the other request methods send the credential in scope. A configure callback that
sets either header takes over both, and the harness adds nothing:

```csharp
var response = await app.Get("/todos", request => request.Headers["X-Test-Grants"] = "todos:read");
```

## A credential built in the test

```csharp
var writer = app.CreateClient<TodosClient>(new TestCredential(["todos:write"], "pia"));
var http = app.CreateHttpClient(TestCredential.Anonymous);
```

`TestCredential` is the value behind the attributes: a grant list and an optional subject.
`TestCredential.Anonymous` sends nothing.

## Next

- [Typed clients](/guide/testing-clients): the clients the attributes apply to
- [Authentication](/guide/authentication): the principal sources a test leaves alone
- [Authorization](/guide/authorization): what the grants are judged against

global using Hardened.Shared.Testing.Attributes;
global using Hardened.Web.Testing;
global using Xunit;
#if (hasClient)
global using Hardened.Requests.Abstract.Responses;
global using Hardened1.Client;
// Aliased, because the generated models are named after the schemas and the schemas are named
// after this application's own types: a bare NewTodo in a test is the application's record, and
// Generated is a namespace the build already declares.
global using ClientModels = Hardened1.Client.Models;
#endif

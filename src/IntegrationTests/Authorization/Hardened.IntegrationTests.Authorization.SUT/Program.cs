using Hardened.IntegrationTests.Authorization.SUT;
using Hardened.Web.AspNetCore.Runtime;

var builder = Application.CreateBuilder(args);

var app = builder.Build();

app.UseHardened();

app.Run();

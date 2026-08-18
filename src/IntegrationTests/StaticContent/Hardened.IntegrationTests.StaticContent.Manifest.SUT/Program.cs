using Hardened.IntegrationTests.StaticContent.Manifest.SUT;
using Hardened.Web.AspNetCore.Runtime;

var builder = Application.CreateBuilder(args);

var app = builder.Build();

app.UseHardened();

app.Run();

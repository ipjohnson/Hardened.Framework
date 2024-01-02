using Hardened.IntegrationTests.WebApp.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;

var hardenedApp = new Application();

var builder = WebApplication.CreateBuilder(args);

hardenedApp.ConfigureModule(new EnvironmentImpl(), builder.Services);

var app = builder.Build();

app.UseHardened();

app.Run();
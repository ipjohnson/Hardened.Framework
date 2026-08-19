using Hardened.Amz.Web.Lambda.Harness;
using Hardened.Shared.Runtime.Application;
using Hardened1.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

// Listens on 5080. Override with PORT.
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var configured) ? configured : 5080;

var builder = WebApplication.CreateBuilder(args);

// Before the application is added, because the module system reads the environment while it is
// deciding what to register - and on Lambda there is no Program.cs of your own to do it in, so
// without this the modules see the process default rather than HARDENED_ENVIRONMENT.
builder.Services.AddHardenedEnvironment(args);

builder.Services.AddLambdaApplication<Application>();

var app = builder.Build();

app.UseLambdaApplication();

app.Run($"http://localhost:{port}");

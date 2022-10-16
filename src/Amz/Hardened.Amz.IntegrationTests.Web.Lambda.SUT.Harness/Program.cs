using Hardened.IntegrationTests.Web.Lambda.SUT;
using Hardened.Web.Lambda.Harness;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLambdaApplication<Application>();

var app = builder.Build();


app.UseLambdaApplication();

app.Run();

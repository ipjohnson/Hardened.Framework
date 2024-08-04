using Hardened.Amz.Web.Lambda.Harness;
using LambdaWebTest;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLambdaApplication<Application>();
var app = builder.Build();

app.UseLambdaApplication();

app.Run();
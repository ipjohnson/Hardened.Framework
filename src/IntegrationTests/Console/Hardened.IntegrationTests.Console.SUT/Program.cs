using Hardened.IntegrationTests.Console.SUT;

var application = new Application(args);

var results = await application.Run();

await application.DisposeAsync();

return results;

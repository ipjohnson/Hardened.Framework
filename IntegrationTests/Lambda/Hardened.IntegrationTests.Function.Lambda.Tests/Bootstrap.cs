using Hardened.IntegrationTests.Function.Lambda.SUT;
using Hardened.Shared.Testing;

// Applying this for testing individual services vs. a lambda entry point
[assembly: TestApplication(typeof(Application))]
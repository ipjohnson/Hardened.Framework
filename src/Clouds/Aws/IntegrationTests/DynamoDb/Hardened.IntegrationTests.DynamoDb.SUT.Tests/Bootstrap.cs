using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.DynamoDb.SUT;
using Hardened.Shared.Testing.Attributes;

// The whole of the harness setup. [LambdaTesting] is what makes this a change feed test rather than
// a pipeline one: the message is marshalled into DynamoDB's type-tagged wire form and delivered
// through the real invocation loop, so the adapter and its unmarshalling run for real.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(ChangeTestApp))]

# Writing a test attribute

An attribute that sets a test up is a class implementing one of five interfaces. The runner calls
it at the point in the test's construction the interface names, for every test that carries it.

```csharp
public sealed class SeededDatabaseAttribute : Attribute, IHardenedTestStartupAttribute {

    public int Order => 100;

    public async Task Startup(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceProvider provider) {
        await provider.GetRequiredService<ISeeder>().Seed();
    }
}
```

```csharp
[HardenedTest]
[SeededDatabase]
public async Task FindsASeededOrder(IOrderRepository orders) {
    Assert.NotNull(await orders.Get("ORDER#1"));
}
```

## The five interfaces

In the order the runner calls them:

| Interface | Called | To |
|---|---|---|
| `IHardenedTestEnvironmentAttribute` | before the modules are applied, with the environment name and its values | add or change environment values |
| `IHardenedTestDependencyRegistrationAttribute` | after the application's modules have registered, with the `IServiceCollection` | register or replace a service |
| `IHardenedParameterProviderAttribute` | with the collection, and later once per parameter | supply a parameter the container does not hold |
| `IHardenedTestConfigurationAttribute` | with an `IAppConfig` | amend a configuration model for the test |
| `IHardenedTestStartupAttribute` | after the provider is built and the application's startup services have run | start a container, create a table, seed data |

All five extend `IHardenedOrderedAttribute`. `Order` sorts the startup attributes, lowest first.

Every interface is read from the method, its class and the assembly. `AttributeCollection` holds
the three sets, and `GetAttribute<T>()` finds one of a kind across them, which is how an attribute
reads a `[EnvironmentName]` or a setting of its own declared beside it.

## A registration attribute

```csharp
public sealed class InMemoryStoreAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute {

    public int Order => 0;

    public void RegisterDependencies(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceCollection services) {
        services.AddSingleton<IOrderStore, InMemoryOrderStore>();
    }
}
```

It runs after the application's modules, so the registration replaces the application's own.
[Substituting services](/guide/testing-mocks#a-fake-instead-of-a-mock) has a `TimeProvider`
registered this way.

## An environment attribute

```csharp
public sealed class FeatureFlagsOnAttribute : Attribute, IHardenedTestEnvironmentAttribute {

    public int Order => 0;

    public void ConfigureEnvironment(
        AttributeCollection attributes, MethodInfo method,
        string environmentName, IDictionary<string, object> environment) {
        environment["FEATURE_X"] = "on";
        environment["FEATURE_Y"] = "on";
    }
}
```

`[EnvironmentValue]` sets one value. An environment attribute sets a set of them, once, under one
name.

## A configuration attribute

```csharp
public sealed class ShortRetriesAttribute : Attribute, IHardenedTestConfigurationAttribute {

    public int Order => 0;

    public void Configure(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IAppConfig appConfig) {
        appConfig.Amend((RetryConfiguration retry) => retry.MaxAttempts = 1);
    }
}
```

The `IAppConfig` is registered as the test's `IConfigurationPackage`, so the amender runs the
first time the model is resolved. See [Amending configuration](/guide/configuration#amending-configuration).

## Deriving from a shipped attribute

`[LocalDynamoDb]` is a startup attribute meant to be derived from. Override `DdbSetup` to create
the tables the test needs; see [DynamoDB Local](/aws/testing#dynamodb-local).

## Next

- [Substituting services](/guide/testing-mocks): `[Mock]` for the one-line case
- [Environments in tests](/guide/testing#environments-in-tests): the shipped environment attributes
- [Testing AWS handlers](/aws/testing): the Lambda and DynamoDB Local attributes

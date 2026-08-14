# Configuration

A configuration model is a `partial class` of private fields. The generator turns it into an
interface, a property implementation, and the code that reads the environment variables it names.

```csharp
using Hardened.Shared.Runtime.Attributes;

[ConfigurationModel]
public partial class DynamoDbOptions {
    [FromEnvironmentVariable("DYNAMODB_SERVICE_URL")]
    private string _serviceUrl = "";

    [FromEnvironmentVariable("AWS_REGION")]
    private string _region = "";

    private int _retentionDays = 180;
}
```

## What that becomes

Three things, none of which you write:

**An interface**, named `I` + the class name. `DynamoDbOptions` produces `IDynamoDbOptions`. This is
what everything else depends on, so a consumer cannot reach past the interface and mutate the model.

**A property per field**, named from the field with its leading underscore removed and its first
letter capitalised. `_serviceUrl` becomes `ServiceUrl`, `_retentionDays` becomes `RetentionDays`.
The field's initialiser is the default.

**A registration**, so that `IOptions<IDynamoDbOptions>` resolves:

```csharp
public sealed class DynamoDbClientProvider(
    IOptions<IDynamoDbOptions> options,
    IServiceProvider serviceProvider) : IDynamoDbClientProvider {

    // options.Value.ServiceUrl, options.Value.Region, …
}
```

`IOptions<T>` is the familiar shape, so a class that takes configuration looks like any other .NET
class. `IConfigurationManager.GetConfiguration<T>()` is the direct route when you need one:

```csharp
var options = provider.GetRequiredService<IConfigurationManager>()
    .GetConfiguration<IDynamoDbOptions>();
```

::: warning An unregistered model throws when it is first asked for
`GetConfiguration<T>` throws `"<T> is not a registered configuration type"` if no module in the
application contributed that model. The usual cause is that the model lives in an assembly whose
module was never imported.
:::

## Environment variables

`[FromEnvironmentVariable("NAME")]` reads the variable when the model is first constructed, falling
back to the field's initialiser when the variable is unset or empty. The value is converted to the
field's type, so an `int` field backed by `RETENTION_DAYS=90` arrives as `90`.

Reading happens once. Configuration models are cached per type for the life of the application, so a
variable changed after startup is not picked up — which is what you want on Lambda, where the
process outlives many invocations.

To keep a field out of the generated interface entirely — a factory, a secret, something with no
sensible property — mark it `[HideConfigurationField]`.

### Fields that are not simple values

A field can hold a delegate, which is how a model expresses "the default is computed, and the
application may replace it wholesale":

```csharp
[ConfigurationModel]
public partial class DynamoDbOptions {
    private Dictionary<string, Func<IServiceProvider, IAmazonDynamoDB>> _clients = new();

    private Func<IServiceProvider, IAmazonDynamoDB>? _defaultClient;
}
```

## Amending configuration

An application often needs to change a model a library defined — add a response header, register a
named client, raise a timeout — without editing the library. That is what an *amender* is for.

`AppConfig` implements both `IAppConfig` (the builder) and `IConfigurationPackage` (what
`ConfigurationManager` consumes), so an application contributes one from its module:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Configuration;

[HardenedModule]
public partial class Application : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        var config = new AppConfig();

        config.Amend((ResponseHeaderConfiguration response) =>
            response.Add("Access-Control-Allow-Origin", "*"));

        config.Amend((DynamoDbOptions options) =>
            options.Clients["audit"] = provider =>
                new AmazonDynamoDBClient(assumedRoleCredentials, new AmazonDynamoDBConfig()));

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

Amenders run against the concrete model — `DynamoDbOptions`, not `IDynamoDbOptions` — because
amending is the one place that is allowed to write. Every registered `IConfigurationPackage`
contributes its amenders, and all of them run, in registration order, the first time the model is
resolved.

### Amending only in one environment

`Amend` takes an environment name. Passing one restricts the amender to that environment:

```csharp
config.Amend((DynamoDbOptions options) => options.ServiceUrl = "http://localhost:8000", "development");
```

The overload taking a function receives the environment, for when the value itself depends on it:

```csharp
config.Amend((IHardenedEnvironment env, RetryConfiguration retry) => {
    retry.MaxAttempts = env.Matches("production") ? 5 : 1;
    return retry;
});
```

### Replacing a model outright

`ProvideValue` supplies the implementation rather than amending the default:

```csharp
config.ProvideValue<IRateTableConfiguration, RateTableConfiguration>(
    env => new RateTableConfiguration(env.Value("RATE_TABLE", "rates")));
```

## Where models come from

The generator collects every `[ConfigurationModel]` in an assembly into the module's generated
`ConfigurationProvider`. Import the module and its models come with it, which is why
`[DynamoDbModule]` is enough to make `IDynamoDbOptions` resolvable — the model is declared in that
package, not in yours.

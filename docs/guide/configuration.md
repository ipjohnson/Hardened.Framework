# Configuration

A configuration model is a `partial class` of private fields. The generator turns it into an
interface, the properties, and the code that reads the environment variables it names.

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

```csharp
public sealed class DynamoDbClientProvider(IOptions<IDynamoDbOptions> options) : IDynamoDbClientProvider {

    // options.Value.ServiceUrl, options.Value.Region, options.Value.RetentionDays
}
```

`DYNAMODB_SERVICE_URL=http://localhost:8000` sets `ServiceUrl`. `RetentionDays` is 180 unless the
application amends it.

## What the generator writes

| From | To |
|---|---|
| `DynamoDbOptions` | `IDynamoDbOptions`, the interface everything else depends on |
| `_serviceUrl` | `ServiceUrl`. The underscore goes and the first letter is capitalised. The initialiser is the default |
| the class | a registration, so `IOptions<IDynamoDbOptions>` resolves |

`IConfigurationManager.GetConfiguration<T>()` is the direct route when you need one:

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

`[FromEnvironmentVariable("NAME")]` reads the variable when the model is first constructed, and
falls back to the field's initialiser when the variable is unset or empty. The value is converted
to the field's type, so an `int` field backed by `RETENTION_DAYS=90` arrives as `90`.

Reading happens once. Models are cached per type for the life of the application, so a variable
changed after startup is not picked up.

`[HideConfigurationField]` keeps a field out of the generated interface: a factory, a secret,
anything with no sensible property.

A field can hold a delegate, for a default that is computed and that an application may replace:

```csharp
[ConfigurationModel]
public partial class DynamoDbOptions {
    private Dictionary<string, Func<IServiceProvider, IAmazonDynamoDB>> _clients = new();

    private Func<IServiceProvider, IAmazonDynamoDB>? _defaultClient;
}
```

## Amending configuration

An application changes a model a library defined without editing the library: add a response
header, register a named client, raise a timeout. `AppConfig` collects the amendments and is
registered as an `IConfigurationPackage`:

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

Amenders run against the concrete model, `DynamoDbOptions` rather than `IDynamoDbOptions`. Every
registered `IConfigurationPackage` contributes its amenders, and all of them run in registration
order the first time the model is resolved.

### In one environment

`Amend` takes an environment name:

```csharp
config.Amend((DynamoDbOptions options) => options.ServiceUrl = "http://localhost:8000", "development");
```

The overload taking a function receives the environment, for a value that depends on it:

```csharp
config.Amend((IHardenedEnvironment env, RetryConfiguration retry) => {
    retry.MaxAttempts = env.Matches("production") ? 5 : 1;
    return retry;
});
```

### Replacing a model outright

`ProvideValue` supplies the implementation instead of amending the default:

```csharp
config.ProvideValue<IRateTableConfiguration, RateTableConfiguration>(
    env => new RateTableConfiguration(env.Value("RATE_TABLE", "rates")));
```

## Where models come from

The generator collects every `[ConfigurationModel]` in an assembly into the module's generated
`ConfigurationProvider`. Import the module and its models come with it, which is why
`[DynamoDbModule]` is enough to make `IDynamoDbOptions` resolvable.

## Next

- [Environments](/guide/environments): where the variables and the environment name come from
- [Modules](/guide/modules): the module a model belongs to
- [Writing a test attribute](/guide/testing-attributes#a-configuration-attribute): amending a model for one test

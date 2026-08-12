using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing.Tests.Infrastructure;

/// <summary>
/// The mark a <see cref="RecordingRegistrationAttribute"/> leaves behind.
/// </summary>
/// <remarks>
/// Recorded into the service collection rather than into a field on the attribute. Reflection hands
/// out a fresh attribute instance per call, so the object a test holds is never the object the
/// harness invoked, and a field on it would still be empty after the harness had run.
/// </remarks>
public sealed record RegistrationMark(string Name);

/// <summary>
/// The mark a <see cref="RecordingParameterProviderAttribute"/> leaves behind.
/// </summary>
public sealed record ParameterProviderMark(string Name);

/// <summary>
/// Collects what ran during startup, in the order it ran.
/// </summary>
public sealed class StartupLog {
    private readonly List<string> _names = new();

    public IReadOnlyList<string> Names {
        get {
            lock (_names) {
                return _names.ToArray();
            }
        }
    }

    public void Add(string name) {
        lock (_names) {
            _names.Add(name);
        }
    }
}

/// <summary>
/// Configuration an amender can be seen to have touched.
/// </summary>
public sealed class ConfigurationLog {
    public List<string> Names { get; } = new();
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RecordingRegistrationAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute {
    public RecordingRegistrationAttribute(string name) {
        Name = name;
    }

    public string Name { get; }

    public int Order { get; set; } = 10;

    public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
        IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton(new RegistrationMark(Name));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RecordingParameterProviderAttribute : Attribute, IHardenedParameterProviderAttribute {
    public RecordingParameterProviderAttribute(string name) {
        Name = name;
    }

    public string Name { get; }

    public int Order { get; set; } = 10;

    public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
        ParameterInfo? parameterInfo, IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton(new ParameterProviderMark(
            parameterInfo == null ? Name : $"{Name}:{parameterInfo.Name}"));
    }

    public object? ProvideParameterValue(MethodInfo methodInfo, ParameterInfo parameterInfo,
        IApplicationRoot applicationRoot) {
        return null;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RecordingStartupAttribute : Attribute, IHardenedTestStartupAttribute {
    public RecordingStartupAttribute(string name) {
        Name = name;
    }

    public string Name { get; }

    public int Order { get; set; } = 10;

    public Task Startup(AttributeCollection attributeCollection, MethodInfo methodInfo,
        IHardenedEnvironment environment, IServiceProvider serviceProvider) {
        serviceProvider.GetRequiredService<StartupLog>().Add(Name);

        return Task.CompletedTask;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RecordingEnvironmentAttribute : Attribute, IHardenedTestEnvironmentAttribute {
    public RecordingEnvironmentAttribute(string variable, string value) {
        Variable = variable;
        Value = value;
    }

    public string Variable { get; }

    public string Value { get; }

    public int Order { get; set; } = 10;

    public void ConfigureEnvironment(AttributeCollection attributeCollection, MethodInfo methodInfo,
        string environmentName, IDictionary<string, object> environment) {
        environment[Variable] = Value;
        environment["environment-name-seen-by-configure"] = environmentName;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RecordingConfigurationAttribute : Attribute, IHardenedTestConfigurationAttribute {
    public RecordingConfigurationAttribute(string name) {
        Name = name;
    }

    public string Name { get; }

    public int Order { get; set; } = 10;

    public void Configure(AttributeCollection attributeCollection, MethodInfo methodInfo,
        IHardenedEnvironment environment, IAppConfig appConfig) {
        appConfig.Amend<ConfigurationLog>(log => log.Names.Add(Name));
    }
}

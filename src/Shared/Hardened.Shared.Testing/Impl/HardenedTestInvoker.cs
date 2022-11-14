using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Logging;
using Hardened.Shared.Testing.Utilties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Impl;

public class HardenedTestInvoker : XunitTestInvoker
{
    private readonly TestOutputHelper _testOutputHelper;
    private object? _testClassInstance;
    private IApplicationRoot? _testApplicationRoot;
    private readonly HardenedTestEnvironmentBuilder _environmentBuilder = new();
    private readonly Dictionary<ParameterInfo, IHardenedParameterProviderAttribute> _knownParameterValues = new();

    public HardenedTestInvoker(ITest test, 
        TestOutputHelper testOutputHelper,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments, 
        MethodInfo testMethod, 
        object[] testMethodArguments,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, 
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterAttributes, aggregator, cancellationTokenSource)
    {
        _testOutputHelper = testOutputHelper;
    }

    protected override object CreateTestClass()
    {
        return _testClassInstance = base.CreateTestClass();
    }

    protected override async Task<decimal> InvokeTestMethodAsync(object testClassInstance)
    {
        try
        {
            await ConstructParameters();
            
            return await base.InvokeTestMethodAsync(testClassInstance);
        }
        finally
        {
            if (_testApplicationRoot != null)
            {
                await _testApplicationRoot.DisposeAsync();
            }
        }
    }
    public Action<IEnvironment, IServiceCollection> BuildOverrideAction(AttributeCollection attributeCollection, MethodInfo testMethod, IEnvironment environment)
    {
        var registrationAttributeList =
            attributeCollection.GetAttributes<IHardenedTestDependencyRegistrationAttribute>();

        var globalParameterProviderList =
            attributeCollection.GetAttributes<IHardenedParameterProviderAttribute>();

        foreach (var parameterInfo in testMethod.GetParameters())
        {
            var providerAttribute =
                parameterInfo.GetCustomAttributes().OfType<IHardenedParameterProviderAttribute>().FirstOrDefault();

            if (providerAttribute != null)
            {
                _knownParameterValues[parameterInfo] = providerAttribute;
            }
        }

        var configurationAction = CreateConfigurationAction(attributeCollection, environment);

        return (env, collection) =>
        {
            collection.AddSingleton<ITestOutputHelper>(_testOutputHelper);
            collection.AddSingleton(new TestCancellationToken(CancellationTokenSource.Token));
            
            registrationAttributeList.Foreach(hardenedTestDependencyRegistrationAttribute =>
            {
                hardenedTestDependencyRegistrationAttribute.RegisterDependencies(
                    attributeCollection,
                    testMethod,
                    env,
                    collection
                );
            });

            globalParameterProviderList.Foreach(attr =>
            {
                attr.RegisterDependencies(attributeCollection,
                    testMethod,
                    null,
                    env,
                    collection
                );
            });

            _knownParameterValues.Foreach(kvp =>
            {
                kvp.Value.RegisterDependencies(
                    attributeCollection,
                    testMethod,
                    kvp.Key,
                    env,
                    collection
                );
            });

            configurationAction(env, collection);

            collection.RemoveAll<ILoggerProvider>();

            collection.AddSingleton<ILoggerProvider, XunitLoggerProvider>();
            
            ProcessInstanceRegistrationMethod(attributeCollection, testMethod, env, collection);
        };
    }

    private Action<IEnvironment, IServiceCollection> CreateConfigurationAction(
        AttributeCollection attributeCollection, IEnvironment environment)
    {
        return (env, collection) =>
        {   
            var appConfig = new AppConfig();

            attributeCollection.GetAttributes<IHardenedTestConfigurationAttribute>().Foreach(a =>
            {
                a.Configure(attributeCollection, TestMethod, env, appConfig);
            });

            var configureMethod = 
                _testClassInstance!.GetType().FindInstanceMethod("Configure");

            if (configureMethod != null)
            {
                var parameters = new List<object>();

                switch (configureMethod.GetParameters().Length)
                {
                    case 4:
                        parameters.Add(attributeCollection);
                        parameters.Add(TestMethod);
                        parameters.Add(env);
                        parameters.Add(appConfig);
                        break;
                    case 3:
                        parameters.Add(TestMethod);
                        parameters.Add(env);
                        parameters.Add(appConfig);
                        break;
                    case 2:
                        parameters.Add(env);
                        parameters.Add(appConfig);
                        break;
                    case 1:
                        parameters.Add(appConfig);
                        break;
                }

                configureMethod.Invoke(_testClassInstance, parameters.ToArray());
            }

            collection.AddSingleton<IConfigurationPackage>(appConfig);
        };
    }

    private void ProcessInstanceRegistrationMethod(AttributeCollection attributeCollection, MethodInfo testMethod, IEnvironment env, IServiceCollection collection)
    {
        var classInstanceType = _testClassInstance!.GetType();
        var registrationMethod =
            classInstanceType.FindInstanceMethod("RegisterDependencies");

        if (registrationMethod != null)
        {
            var parameters = registrationMethod.GetParameters();
            var invokeParameters = new List<object>();

            switch (parameters.Length)
            {
                case 4:
                    invokeParameters.Add(attributeCollection);
                    invokeParameters.Add(testMethod);
                    invokeParameters.Add(env);
                    invokeParameters.Add(collection);
                    break;
                case 3:
                    invokeParameters.Add(testMethod);
                    invokeParameters.Add(env);
                    invokeParameters.Add(collection);
                    break;
                case 2:
                    invokeParameters.Add(env);
                    invokeParameters.Add(collection);
                    break;
                case 1:
                    invokeParameters.Add(collection);
                    break;
            }

            registrationMethod.Invoke(_testClassInstance, invokeParameters.ToArray());
        }
    }

    private async Task ConstructParameters()
    {
        var attributeCollection = AttributeCollection.FromMethodInfo(TestMethod);

        var environment = 
            _environmentBuilder.BuildEnvironment(attributeCollection, TestMethod, _testClassInstance!);

        var dependencyOverrideAction =
            BuildOverrideAction(attributeCollection, TestMethod, environment);

        await ConstructRootApplication(attributeCollection, environment, dependencyOverrideAction);

        AssignParametersToField(attributeCollection);
    }

    private void AssignParametersToField(AttributeCollection attributeCollection)
    {
        var methodArguments = new List<object>();

        if (TestMethodArguments is { Length: > 0 })
        {
            methodArguments.AddRange(TestMethodArguments);
        }

        var parameters = TestMethod.GetParameters();

        var globalParameterProviderList = attributeCollection.GetAttributes<IHardenedParameterProviderAttribute>();

        for (var i = 0; methodArguments.Count < parameters.Length; i++)
        {
            var parameter = parameters[i];
            object? argumentValue = null;

            if (_knownParameterValues.TryGetValue(parameter, out var providerAttribute))
            {
                argumentValue = providerAttribute.ProvideParameterValue(TestMethod, parameter, _testApplicationRoot!);
            }

            if (argumentValue == null && globalParameterProviderList.Count > 0)
            {
                foreach (var attribute in globalParameterProviderList)
                {
                    argumentValue = attribute.ProvideParameterValue(TestMethod, parameter, _testApplicationRoot!);

                    if (argumentValue != null)
                    {
                        break;
                    }
                }
            }

            argumentValue ??= LocateValue(_testApplicationRoot!, parameter);

            if (argumentValue == null)
            {
                throw new Exception($"Could not resolve parameter {parameter.ParameterType.Name} {parameter.Name}");
            }

            methodArguments.Insert(i, argumentValue);
        }

        TestMethodArguments = methodArguments.ToArray();
    }

    private Task ConstructRootApplication(AttributeCollection attributeCollection, IEnvironment environment, Action<IEnvironment, IServiceCollection> dependencyOverrideAction)
    {
        _testApplicationRoot = ProcessParameterTypesForRoot(attributeCollection, environment, dependencyOverrideAction);

        if (_testApplicationRoot == null)
        {
            _testApplicationRoot =
                ConstructApplicationFromEntryPoint(attributeCollection, environment, dependencyOverrideAction);
        }

        return StartApplication(attributeCollection, environment);
    }

    private IApplicationRoot ConstructApplicationFromEntryPoint(AttributeCollection attributeCollection, IEnvironment environment, Action<IEnvironment, IServiceCollection> overrideAction)
    {
        var entryPoint = attributeCollection.GetAttribute<HardenedTestEntryPointAttribute>();

        if (entryPoint == null)
        {
            throw new Exception(
                $"Could not find {nameof(HardenedTestEntryPointAttribute)}, did you apply it to your assembly?");
        }

        if (typeof(IApplicationRoot).IsAssignableFrom(entryPoint.EntryPoint))
        {
            return (IApplicationRoot)Activator.CreateInstance(entryPoint.EntryPoint, environment, overrideAction)!;
        }
            
        if (typeof(IApplicationModule).IsAssignableFrom(entryPoint.EntryPoint))
        {
            var module = (IApplicationModule)Activator.CreateInstance(entryPoint.EntryPoint)!;

            return new TestApplication(module, module.GetType().Namespace + ".test", environment, overrideAction);
        }

        throw new Exception($"Entry point is not correct type");
    }

    private IApplicationRoot? ProcessParameterTypesForRoot(AttributeCollection attributeCollection, IEnvironment environment, Action<IEnvironment, IServiceCollection>  overrideAction)
    {
        foreach (var parameterInfo in TestMethod.GetParameters())
        {
            if (typeof(IApplicationRoot).IsAssignableFrom(parameterInfo.ParameterType))
            {
                var applicationRoot = 
                    (IApplicationRoot?)Activator.CreateInstance(parameterInfo.ParameterType, environment, overrideAction);

                _knownParameterValues[parameterInfo] = new SimpleParameterValueAttribute(applicationRoot!);

                return applicationRoot;
            }
        }

        return null;
    }

    private async Task StartApplication(AttributeCollection attributeCollection, IEnvironment environment)
    {
        foreach (var startupAttribute in 
                 attributeCollection.GetAttributes<IHardenedTestStartupAttribute>().OrderBy(a => a.Order))
        {
            await startupAttribute.Startup(attributeCollection, TestMethod, environment,
                _testApplicationRoot!.Provider);
        }
    }


    private object? LocateValue(IApplicationRoot applicationInstance, ParameterInfo parameter)
    {
        object? returnValue = applicationInstance.Provider.GetService(parameter.ParameterType);

        if (returnValue == null &&
            !parameter.ParameterType.IsInterface)
        {
            returnValue = GenerateConcreteType(applicationInstance, parameter.ParameterType);
        }

        return returnValue;
    }

    private object? GenerateConcreteType(IApplicationRoot applicationInstance, Type parameterParameterType)
    {
        var constructor =
            parameterParameterType.GetConstructors()
                .Where(c => c.IsPublic && !c.IsStatic)
                .MaxBy(c => c.GetParameters().Length);

        if (constructor != null)
        {
            var parameters = new List<object?>();

            foreach (var parameterInfo in constructor.GetParameters())
            {
                var parameterValue = LocateValue(applicationInstance, parameterInfo);

                if (parameterValue == null)
                {
                    if (parameterInfo.HasDefaultValue)
                    {
                        parameterValue = parameterInfo.DefaultValue;
                    }
                    else
                    {
                        throw new Exception(
                            $"Could not locate {parameterInfo.ParameterType} for parameter {parameterInfo.Name} on {constructor.DeclaringType?.Name}");
                    }
                }

                parameters.Add(parameterValue);
            }

            return constructor.Invoke(parameters.ToArray());
        }

        return null;
    }
}
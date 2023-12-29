using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Utilties;

namespace Hardened.Shared.Testing.Impl;

public class HardenedTestEnvironmentBuilder {
    public IEnvironment BuildEnvironment(AttributeCollection attributeCollection, MethodInfo testMethod,
        object testClassInstance) {
        var environmentName = attributeCollection.GetAttribute<EnvironmentNameAttribute>()?.Name ?? "test";
        var environmentValueAttributeList = attributeCollection.GetAttributes<EnvironmentValueAttribute>();
        var configAttributes =
            attributeCollection.GetAttributes<IHardenedTestEnvironmentAttribute>();

        var environmentDictionary = new Dictionary<string, object>();

        foreach (var environmentValueAttribute in environmentValueAttributeList) {
            environmentDictionary[environmentValueAttribute.Variable] = environmentValueAttribute.Value;
        }

        foreach (var configAttribute in configAttributes) {
            configAttribute.ConfigureEnvironment(attributeCollection, testMethod, environmentName,
                environmentDictionary);
        }

        var configureMethod =
            testClassInstance.GetType().FindInstanceMethod("ConfigureEnvironment");

        if (configureMethod != null) {
            var parameters = new List<object>();
            var methodParameters = configureMethod.GetParameters().Length;

            switch (methodParameters) {
                case 4:
                    parameters.Add(attributeCollection);
                    parameters.Add(testMethod);
                    parameters.Add(environmentName);
                    parameters.Add(environmentDictionary);
                    break;
                case 2:
                    parameters.Add(environmentName);
                    parameters.Add(environmentDictionary);
                    break;
                case 1:
                    parameters.Add(environmentDictionary);
                    break;

                default:
                    throw new Exception("ConfigureEnvironment parameters do not match");
            }

            configureMethod.Invoke(testClassInstance, parameters.ToArray());
        }

        return new TestEnvironment(environmentName, environmentDictionary);
    }
}
﻿using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hardened.Shared.Testing.Attributes;

public interface IHardenedParameterProviderAttribute : IHardenedOrderedAttribute {
    void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        ParameterInfo? parameterInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection);

    object? ProvideParameterValue(
        MethodInfo methodInfo,
        ParameterInfo parameterInfo,
        IApplicationRoot applicationRoot);
}
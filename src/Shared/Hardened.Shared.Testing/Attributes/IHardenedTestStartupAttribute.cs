using System.Reflection;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Testing.Attributes;

public interface IHardenedTestStartupAttribute : IHardenedOrderedAttribute {
    Task Startup(AttributeCollection attributeCollection, MethodInfo methodInfo, IHardenedEnvironment environment,
        IServiceProvider serviceProvider);
}
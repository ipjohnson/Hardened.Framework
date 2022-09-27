﻿using System.Reflection;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Testing.Attributes
{
    public interface IHardenedTestStartupAttribute
    {
        Task Startup(AttributeCollection attributeCollection, MethodInfo methodInfo, IEnvironment environment, IServiceProvider serviceProvider);
    }
}

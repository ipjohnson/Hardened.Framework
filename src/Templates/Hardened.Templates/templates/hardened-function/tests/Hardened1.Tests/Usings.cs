global using DependencyModules.Testing.Attributes;
global using Hardened.Shared.Testing.Attributes;
#if (xunit)
global using DependencyModules.xUnit.Attributes;
global using Xunit;
#endif
#if (nunit)
global using DependencyModules.NUnit.Attributes;
global using NUnit.Framework;
#endif

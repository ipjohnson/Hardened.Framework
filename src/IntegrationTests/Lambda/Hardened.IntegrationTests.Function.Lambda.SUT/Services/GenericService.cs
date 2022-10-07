using Hardened.IntegrationTests.Function.Lambda.SUT.Models;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Services;

public interface IGenericService<T>
{

}

[Expose]
public class GenericService<T> : IGenericService<T>
{

}

[Expose]
public class ClosedGeneric : IGenericService<PersonModel>
{

}
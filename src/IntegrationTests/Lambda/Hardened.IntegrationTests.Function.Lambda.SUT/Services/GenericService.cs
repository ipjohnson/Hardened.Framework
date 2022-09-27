using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Services
{
    public interface IGenericService<T>
    {

    }

    [Expose(typeof(GenericService<>), Try = true)]
    public class GenericService<T> : IGenericService<T>
    {

    }
}

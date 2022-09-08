using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing
{
    public interface ITestServiceProvider
    {
        void InitializeProvider(object initValue);

        void RegisterService(IServiceCollection collection);
    }
}

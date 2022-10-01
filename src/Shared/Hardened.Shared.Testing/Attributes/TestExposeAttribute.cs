namespace Hardened.Shared.Testing.Attributes;

//[AttributeUsage(AttributeTargets.Method)]
//public class TestExposeAttribute : Attribute, ITestExposeAttribute
//{
//    public TestExposeAttribute(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Singleton)
//    {
//        ServiceType = serviceType;
//        ImplementationType = implementationType;
//        Lifetime = lifetime;
//    }

//    public Type ServiceType { get; }

//    public Type ImplementationType { get; }

//    public ServiceLifetime Lifetime { get; }

//    void ITestExposeAttribute.ExposeDependencies(MethodInfo method, IServiceCollection services)
//    {
//        services.Add(new ServiceDescriptor(ServiceType, ImplementationType, Lifetime));
//    }
//}
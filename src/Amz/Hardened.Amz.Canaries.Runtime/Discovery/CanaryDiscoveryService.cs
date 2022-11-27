using Hardened.Amz.Canaries.Runtime.Attributes;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Shared.Runtime.Attributes;
using System.Reflection;

namespace Hardened.Amz.Canaries.Runtime.Discovery;

public interface ICanaryDiscoveryService
{
    IReadOnlyDictionary<string, CanaryDefinition> CanaryDefinitions { get; }
}

[Expose]
[Singleton]
public class CanaryDiscoveryService : ICanaryDiscoveryService
{
    public CanaryDiscoveryService(IEnumerable<ICanaryTypeRegistration> typeRegistrations)
    {
        CanaryDefinitions = GenerateCanaryRegistrations(typeRegistrations);
    }

    private IReadOnlyDictionary<string, CanaryDefinition> 
        GenerateCanaryRegistrations(IEnumerable<ICanaryTypeRegistration> assemblyRegistrations)
    {
        var canaryDefinitions = new Dictionary<string, CanaryDefinition>();

        foreach (ICanaryTypeRegistration typeRegistration in assemblyRegistrations)
        {
            foreach (var exportedType in typeRegistration.ScanTypes())
            {
                ProcessExportedType(exportedType, canaryDefinitions);
            }
        }
        
        return canaryDefinitions;
    }

    private void ProcessExportedType(
        Type exportedType, Dictionary<string,CanaryDefinition> canaryDefinitions)
    {
        foreach (var method in 
                 exportedType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var canaryAttribute = method.GetCustomAttribute<HardenedCanaryAttribute>();

            if (canaryAttribute != null &&
                string.IsNullOrEmpty(canaryAttribute.Skip))
            {
                var declaringType =
                    method.DeclaringType ?? 
                    throw new Exception("DeclaringType cannot be null, only methods in classes are supported");

                var canaryName = canaryAttribute.Name;

                if (string.IsNullOrEmpty(canaryName))
                {
                    canaryName = method.Name;
                }
                
                if (canaryDefinitions.ContainsKey(canaryName))
                {
                    throw new Exception(
                        $"Canary already exists with name {canaryName}, overloaded methods are not supported");
                }
                
                canaryDefinitions[canaryName] =
                    GetCanaryInformationForMethod(declaringType, method, canaryAttribute);
            }
        }
    }

    private CanaryDefinition GetCanaryInformationForMethod(Type declaringType, MethodInfo method,
        HardenedCanaryAttribute canaryAttribute)
    {
        return new CanaryDefinition(
            declaringType.FullName!,
            method.Name,
            new CanaryFrequency(
                canaryAttribute.Frequency, 
                canaryAttribute.Unit,
                canaryAttribute.FlightStyle,
                canaryAttribute.AllowConcurrentExecution),
            canaryAttribute.ReportMetric
        );
    }

    public IReadOnlyDictionary<string, CanaryDefinition> CanaryDefinitions { get; }
}
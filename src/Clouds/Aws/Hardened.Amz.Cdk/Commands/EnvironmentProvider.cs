using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Cdk.Commands;

public interface IDeploymentAccountProvider {
    string GetDeploymentAccount(object configuration, string accountType);
}

[Expose]
public class DeploymentAccountProvider : IDeploymentAccountProvider {

    public string GetDeploymentAccount(object configuration, string accountType) {
        foreach (var property in configuration.GetType().GetProperties()) {
            if (property.GetMethod == null || 
                property.GetMethod.IsStatic || 
                property.PropertyType != typeof(string)) {
                continue;
            }
            
            if (property.Name == accountType) {
                var value = property.GetValue(configuration, null);

                if (value != null) {
                    return (string)value;
                }
            }
        }
        
        throw new ApplicationException("Configuration value does not contain deployment account type: " + accountType);
    }
}
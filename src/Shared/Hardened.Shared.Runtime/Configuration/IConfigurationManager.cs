namespace Hardened.Shared.Runtime.Configuration;

public interface IConfigurationManager {
    T GetConfiguration<T>() where T : class;
}
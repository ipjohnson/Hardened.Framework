namespace Hardened.Shared.Runtime.Application;

/// <summary>
/// Represents the application logic to execute
/// </summary>
/// <param name="Delegate">Delegate to execute</param>
/// <param name="ShouldStartApp">Should StartUp logic be executed</param>
public record ApplicationDelegate(Func<Task<int>> Delegate, bool ShouldStartApp);

/// <summary>
/// Provides application delegate based on environment and provider
/// </summary>
public interface IApplicationDelegateProvider {
    Task<ApplicationDelegate> ProvideDelegate(IHardenedEnvironment environment, IServiceProvider serviceProvider);
}
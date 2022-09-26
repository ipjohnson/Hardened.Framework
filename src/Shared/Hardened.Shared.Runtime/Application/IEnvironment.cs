using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Application
{
    public interface IEnvironment
    {
        string Name { get; }

        T? Value<T>(string name, T? defaultValue = default);
    }

    public static class IEnvironmentExtensions
    {
        public static bool Matches(this IEnvironment environment, params string[] environments)
        {
            return environments.Any(s => environment.Name.Equals(s, StringComparison.CurrentCultureIgnoreCase));
        }

        public static bool MatchesVariable(this IEnvironment environment, string variable, string value)
        {
            return environment.Value(variable, "")!.Equals(value, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}

using static Hardened.SourceGenerator.Configuration.ConfigurationIncrementalGenerator;

namespace Hardened.SourceGenerator.Configuration
{
    public class ConfigurationFileModelComparer : IEqualityComparer<ConfigurationFileModel>
    {
        public bool Equals(ConfigurationFileModel x, ConfigurationFileModel y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;

            return x.Equals(y);
        }

        public int GetHashCode(ConfigurationFileModel obj)
        {
            return obj.GetHashCode();
        }
    }
}

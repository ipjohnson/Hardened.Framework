namespace Hardened.Shared.Runtime.Attributes
{
    /// <summary>
    /// Used to attribute Expose service, registering them only when the environment match
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ForEnvironmentAttribute : Attribute
    {
        public ForEnvironmentAttribute(string environment)
        {
            Environment = environment;
        }

        public string Environment { get; }
    }
}

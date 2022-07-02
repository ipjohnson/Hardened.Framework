namespace Hardened.Shared.Testing
{
    public class TestApplicationAttribute : Attribute
    {
        public TestApplicationAttribute(Type application)
        {
            Application = application;
        }

        public Type Application { get; }
    }
}

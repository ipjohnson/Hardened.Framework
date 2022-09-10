namespace Hardened.Shared.Testing
{
    public class TestLibraryAttribute : Attribute
    {
        public TestLibraryAttribute(Type libraryType)
        {
            LibraryType = libraryType;
        }

        public Type LibraryType { get; }
    }
}

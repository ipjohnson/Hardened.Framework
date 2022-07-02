namespace Hardened.Shared.Testing
{
    public class TestLibraryAttribute
    {
        public TestLibraryAttribute(Type libraryType)
        {
            LibraryType = libraryType;
        }

        public Type LibraryType { get; }
    }
}

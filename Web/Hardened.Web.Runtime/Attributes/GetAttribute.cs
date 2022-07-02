namespace Hardened.Web.Runtime.Attributes
{
    public class GetAttribute : Attribute
    {
        public GetAttribute(string path = "")
        {
            Path = path;
        }

        public string Path { get; }

        public int SuccessStatus { get; set; } = 200;

        public int ValidationErrorStatus { get; set; } = 400;

        public int NullReturnStatus { get; set; } = 404;

        public int ErrorStatus { get; set; } = 500;
    }
}

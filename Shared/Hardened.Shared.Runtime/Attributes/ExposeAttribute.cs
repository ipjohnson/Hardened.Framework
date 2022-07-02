namespace Hardened.Shared.Runtime.Attributes
{
    public class ExposeAttribute : Attribute
    {
        public ExposeAttribute(params Type[] forServices)
        {
            ForServices = forServices;
        }

        public Type[] ForServices { get; }
    }
}

namespace Hardened.Requests.Abstract.Attributes
{
    public class AuthorizeActivitiesAttribute : Attribute
    {
        public AuthorizeActivitiesAttribute(params string[] activities)
        {
            Activities = activities;
        }

        public string[] Activities { get; }
    }
}

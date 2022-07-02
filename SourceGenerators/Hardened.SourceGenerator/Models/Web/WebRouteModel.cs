using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Web
{
    public class WebRouteModel
    {
        public WebRouteModel(ITypeDefinition controllerType, WebRouteInformation routeInformation)
        {
            ControllerType = controllerType;
            RouteInformation = routeInformation;
        }

        public ITypeDefinition ControllerType { get; }

        public WebRouteInformation RouteInformation { get; }
    }
}

using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Models
{
    public class WebEndPointModel
    {
        public static readonly WebEndPointModel Empty =
            new (
                TypeDefinition.Get(typeof(object)),
                TypeDefinition.Get(typeof(object)),
                "None",
                new WebRouteInformation("", "", Array.Empty<PathToken>()),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformation(),
                new List<FilterInformationModel>()
            );

        public WebEndPointModel(
            ITypeDefinition handlerType,
            ITypeDefinition controllerType, 
            string handlerMethod,
            WebRouteInformation routeInformation,
            IReadOnlyList<RequestParameterInformation> requestParameterInformationList,
            ResponseInformation responseInformation, 
            IReadOnlyCollection<FilterInformationModel> filters)
        {
            HandlerType = handlerType;
            ControllerType = controllerType;
            RouteInformation = routeInformation;
            RequestParameterInformationList = requestParameterInformationList;
            ResponseInformation = responseInformation;
            Filters = filters;
            HandlerMethod = handlerMethod;
        }

        public ITypeDefinition ControllerType { get; }

        public string HandlerMethod { get;  }

        public ITypeDefinition HandlerType { get; }

        public WebRouteInformation RouteInformation { get; }

        public IReadOnlyList<RequestParameterInformation> RequestParameterInformationList { get; }

        public ResponseInformation ResponseInformation { get; }

        public IReadOnlyCollection<FilterInformationModel> Filters { get; }
    }

    public class PathToken
    {
        public PathToken(string name, int order, bool optional)
        {
            Name = name;
            Order = order;
            Optional = optional;
        }

        public string Name { get; }

        public int Order { get; }

        public bool Optional { get; }
    }

    public class WebRouteInformation
    {
        public WebRouteInformation(string pathTemplate, string method, IEnumerable<PathToken> tokens)
        {
            PathTemplate = pathTemplate;
            Method = method;
            Tokens = tokens;
        }

        public string PathTemplate { get; }

        public string Method { get; }

        public IEnumerable<PathToken> Tokens { get; }
    }

    public class RequestParameterInformation
    {
        public RequestParameterInformation(string name, bool required, string? defaultValue, ParameterBindType bindingType, PathToken? pathToken)
        {
            Name = name;
            Required = required;
            DefaultValue = defaultValue;
            BindingType = bindingType;
            PathToken = pathToken;
        }

        public string Name { get; }

        public bool Required { get; }

        public string? DefaultValue { get; }

        public ParameterBindType BindingType { get; }

        public PathToken? PathToken { get; }
    }

    public enum ParameterBindType
    {
        Path,
        QueryString,
        Header,
        Body,
        ServiceProvider,
        ExecutionContext,
        ExecutionRequest,
        ExecutionResponse
    }

    public class ResponseInformation
    {
        public bool IsAsync { get; set; }

        public string? TemplateName { get; set; }

        public ITypeDefinition? ReturnType { get; set; }
    }
}

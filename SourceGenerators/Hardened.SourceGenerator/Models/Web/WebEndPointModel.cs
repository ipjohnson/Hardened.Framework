using CSharpAuthor;

namespace Hardened.SourceGenerator.Models
{
    public class WebEndPointModel
    {
        public static WebEndPointModel Empty =
            new (
                TypeDefinition.Get(typeof(object)),
                TypeDefinition.Get(typeof(object)),
                "None",
                new WebRouteInformation("", "", Array.Empty<PathToken>()),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformation(),
                new List<FilterInformation>()
            );

        public WebEndPointModel(
            ITypeDefinition handlerType,
            ITypeDefinition controllerType, 
            string handlerMethod,
            WebRouteInformation routeInformation,
            IReadOnlyList<RequestParameterInformation> requestParameterInformationList,
            ResponseInformation responseInformation, 
            IReadOnlyCollection<FilterInformation> filters)
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

        public IReadOnlyCollection<FilterInformation> Filters { get; }
    }

    public class FilterInformation
    {
        public FilterInformation(ITypeDefinition typeDefinition, string arguments)
        {
            TypeDefinition = typeDefinition;
            Arguments = arguments;
        }

        public ITypeDefinition TypeDefinition { get; }

        public string Arguments { get; }
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
    }
}

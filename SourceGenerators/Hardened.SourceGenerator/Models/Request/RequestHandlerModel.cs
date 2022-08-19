using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request
{
    public class RequestHandlerModel
    {
        public RequestHandlerModel(
            RequestHandlerNameModel name, 
            ITypeDefinition controllerType, 
            string handlerMethod, 
            ITypeDefinition invokeHandlerType, 
            IReadOnlyList<RequestParameterInformation> requestParameterInformationList, 
            ResponseInformation responseInformation, IReadOnlyCollection<FilterInformationModel> filters)
        {
            Name = name;
            ControllerType = controllerType;
            HandlerMethod = handlerMethod;
            InvokeHandlerType = invokeHandlerType;
            RequestParameterInformationList = requestParameterInformationList;
            ResponseInformation = responseInformation;
            Filters = filters;
        }

        public RequestHandlerNameModel Name { get; }
        
        public ITypeDefinition ControllerType { get; }

        public string HandlerMethod { get; }

        public ITypeDefinition InvokeHandlerType { get; }

        public IReadOnlyList<RequestParameterInformation> RequestParameterInformationList { get; }

        public ResponseInformation ResponseInformation { get; }

        public IReadOnlyCollection<FilterInformationModel> Filters { get; }
    }
}

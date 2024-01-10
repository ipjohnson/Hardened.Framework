using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Models.Request;

public class RequestHandlerModel {
    public RequestHandlerModel(
        RequestHandlerNameModel name,
        ITypeDefinition controllerType,
        string handlerMethod,
        ITypeDefinition invokeHandlerType,
        IReadOnlyList<RequestParameterInformation> requestParameterInformationList,
        ResponseInformationModel responseInformation,
        IReadOnlyList<AttributeModel> filters) {
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

    public ResponseInformationModel ResponseInformation { get; }

    public IReadOnlyList<AttributeModel> Filters { get; }

    public override bool Equals(object obj) {
        if (obj is not RequestHandlerModel requestHandlerModel) {
            return false;
        }

        if (!Name.Equals(requestHandlerModel.Name)) {
            return false;
        }

        if (!ControllerType.Equals(requestHandlerModel.ControllerType)) {
            return false;
        }

        if (!HandlerMethod.Equals(requestHandlerModel.HandlerMethod)) {
            return false;
        }


        if (!InvokeHandlerType.Equals(requestHandlerModel.InvokeHandlerType)) {
            return false;
        }

        if (!ResponseInformation.Equals(requestHandlerModel.ResponseInformation)) {
            return false;
        }

        if (Filters.Count != requestHandlerModel.Filters.Count) {
            return false;
        }

        for (var i = 0; i < requestHandlerModel.Filters.Count; i++) {
            var x = Filters[i];
            var y = requestHandlerModel.Filters[i];

            if (!x.Equals(y)) {
                return false;
            }
        }

        if (RequestParameterInformationList.Count != requestHandlerModel.RequestParameterInformationList.Count) {
            return false;
        }

        for (var i = 0; i < RequestParameterInformationList.Count; i++) {
            var x = RequestParameterInformationList[i];
            var y = requestHandlerModel.RequestParameterInformationList[i];

            if (!x.Equals(y)) {
                return false;
            }
        }

        return true;
    }

    public override string ToString() {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append(Name);
        stringBuilder.Append(":");
        stringBuilder.Append(ControllerType);
        stringBuilder.Append(".");
        stringBuilder.Append(HandlerMethod);

        return stringBuilder.ToString();
    }

    public override int GetHashCode() {
        unchecked {
            var hashCode = Name.GetHashCode();

            hashCode = (hashCode * 397) ^ ControllerType.GetHashCode();
            hashCode = (hashCode * 397) ^ HandlerMethod.GetHashCode();
            hashCode = (hashCode * 397) ^ InvokeHandlerType.GetHashCode();
            hashCode = (hashCode * 397) ^ RequestParameterInformationList.GetHashCodeAggregation();
            hashCode = (hashCode * 397) ^ ResponseInformation.GetHashCode();
            hashCode = (hashCode * 397) ^ Filters.GetHashCodeAggregation();

            return hashCode;
        }
    }
}
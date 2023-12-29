using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Requests;

public class RequestHandlerModelComparer : IEqualityComparer<RequestHandlerModel> {
    public bool Equals(RequestHandlerModel x, RequestHandlerModel y) {
        var equalValue = x.Equals(y);

        return equalValue;
    }

    public int GetHashCode(RequestHandlerModel obj) {
        return obj.GetHashCode();
    }
}
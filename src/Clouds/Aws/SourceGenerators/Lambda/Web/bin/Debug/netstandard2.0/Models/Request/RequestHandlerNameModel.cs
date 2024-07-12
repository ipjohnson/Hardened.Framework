namespace Hardened.SourceGenerator.Models.Request;

public class RequestHandlerNameModel {
    public RequestHandlerNameModel(string path, string method) {
        Path = path;
        Method = method;
    }

    public string Path { get; }

    public string Method { get; }

    public override bool Equals(object obj) {
        if (obj is not RequestHandlerNameModel requestHandlerNameModel) {
            return false;
        }

        var equalValue = Path.Equals(requestHandlerNameModel.Path) && Method.Equals(requestHandlerNameModel.Method);

        return equalValue;
    }

    public override string ToString() {
        return Method + ":" + Path;
    }

    public override int GetHashCode() {
        unchecked {
            return (Path.GetHashCode() * 397) ^ Method.GetHashCode();
        }
    }
}
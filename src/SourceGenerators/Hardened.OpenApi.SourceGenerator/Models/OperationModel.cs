namespace Hardened.OpenApi.SourceGenerator.Models;

internal class OperationModel : IEquatable<OperationModel> {
    public string OperationId { get; set; } = "";
    public string Path { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string? Tag { get; set; }
    public List<ParameterModel> Parameters { get; set; } = new();
    public string? RequestBodyRef { get; set; }
    public string? RequestBodyType { get; set; }
    public string? ResponseRef { get; set; }
    public string? ResponseType { get; set; }
    public string? ResponseFormat { get; set; }
    public bool ResponseIsArray { get; set; }
    public string? ResponseArrayItemsRef { get; set; }
    public int SuccessStatusCode { get; set; } = 200;

    public bool Equals(OperationModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return OperationId == other.OperationId && Path == other.Path &&
               HttpMethod == other.HttpMethod && Tag == other.Tag &&
               Parameters.SequenceEqual(other.Parameters);
    }

    public override bool Equals(object? obj) => Equals(obj as OperationModel);

    public override int GetHashCode() {
        unchecked {
            var hash = OperationId.GetHashCode();
            hash = (hash * 397) ^ Path.GetHashCode();
            hash = (hash * 397) ^ HttpMethod.GetHashCode();
            return hash;
        }
    }
}

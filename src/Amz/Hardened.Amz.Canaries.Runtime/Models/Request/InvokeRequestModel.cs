using Hardened.Amz.Canaries.Runtime.Models.Flight;

namespace Hardened.Amz.Canaries.Runtime.Models.Request;

public record InvokeRequestModel(
    string CanaryName,
    string InvokeId,
    CanaryDefinition Definition);
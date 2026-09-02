using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.OpenApi.ResponseModel.SUT;

/// <summary>
/// The specification-first application under the <c>Response</c> model, which the sibling SUT's
/// <c>Throws</c> mode never exercises: every declared status here is a returned case rather
/// than a throw, so the response-set dispatch runs for every operation.
/// </summary>
[HardenedModule]
[HardenedWebModule]
public partial class LabelsApp { }

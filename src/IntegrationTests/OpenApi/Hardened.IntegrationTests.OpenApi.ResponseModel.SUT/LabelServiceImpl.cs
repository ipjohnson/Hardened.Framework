using Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Models;
using Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.IntegrationTests.OpenApi.ResponseModel.SUT;

/// <summary>
/// Every declared status is returned as a case, never thrown. Deliberately trivial, like the
/// sibling SUT: what is under test is the generated container, its case types and the dispatch
/// around this class.
/// </summary>
/// <remarks>
/// The 404s are <c>NotFound&lt;Problem&gt;</c> rather than a case type per operation. Nothing is
/// generated for them: the description declares a 404 with a <c>Problem</c>, and the framework
/// already ships the type for that - which is the same record a code-first handler in the
/// <c>Responses</c> SUT returns for the same status.
/// </remarks>
[Handler]
public class LabelServiceImpl : ILabelService {

    /// <summary>
    /// The 404 is the framework's own record. The build wrote the conversion into the case the
    /// contract declares, NotFound&lt;Problem&gt;, with the Problem's title and status filled from
    /// the record and the detail from here.
    /// </summary>
    public Task<GetLabelResponse> GetLabel(string labelId) {
        if (labelId == "missing") {
            return Task.FromResult<GetLabelResponse>(new NotFound("label", "No such label"));
        }

        return Task.FromResult<GetLabelResponse>(new GetLabelOk($"Label {labelId}"));
    }

    public Task<CreateLabelResponse> CreateLabel(LabelRequest body) {
        return Task.FromResult<CreateLabelResponse>(body);
    }

    /// <summary>The shared instance, for a handler with nothing to add: no allocation for the 404.</summary>
    public Task<ArchiveLabelResponse> ArchiveLabel(string labelId) {
        if (labelId == "missing") {
            return Task.FromResult<ArchiveLabelResponse>(NotFound.Default);
        }

        return Task.FromResult<ArchiveLabelResponse>(new ArchiveLabelNoContent());
    }
}

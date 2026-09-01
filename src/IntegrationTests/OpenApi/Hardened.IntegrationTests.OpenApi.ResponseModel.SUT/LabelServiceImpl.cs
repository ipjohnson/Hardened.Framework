using Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Models;
using Hardened.IntegrationTests.OpenApi.ResponseModel.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.ResponseModel.SUT;

/// <summary>
/// Every declared status is returned as a case, never thrown. Deliberately trivial, like the
/// sibling SUT: what is under test is the generated container, its case types and the dispatch
/// around this class.
/// </summary>
[Handler]
public class LabelServiceImpl : ILabelService {

    public Task<GetLabelResponse> GetLabel(string labelId) {
        if (labelId == "missing") {
            return Task.FromResult<GetLabelResponse>(
                new GetLabelNotFound(new Problem { Title = "No such label", Status = 404 }));
        }

        return Task.FromResult<GetLabelResponse>(new GetLabelOk($"Label {labelId}"));
    }

    public Task<ArchiveLabelResponse> ArchiveLabel(string labelId) {
        if (labelId == "missing") {
            return Task.FromResult<ArchiveLabelResponse>(
                new ArchiveLabelNotFound(new Problem { Title = "No such label", Status = 404 }));
        }

        return Task.FromResult<ArchiveLabelResponse>(new ArchiveLabelNoContent());
    }
}

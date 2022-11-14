using Amazon.DynamoDBv2.Model;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.DynamoDbClient;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.DynamoDb;

public interface ICanaryDynamoReader
{
    Task<CurrentCanaryState?> GetCanaryState();
}

public class CanaryDynamoReader : ICanaryDynamoReader
{
    private readonly IDynamoDbClientProvider _clientProvider;
    private readonly ICanaryConfigurationModel _canaryConfiguration;

    public CanaryDynamoReader(
        IDynamoDbClientProvider clientProvider, IOptions<ICanaryConfigurationModel> canaryModel)
    {
        _clientProvider = clientProvider;
        _canaryConfiguration = canaryModel.Value;
    }

    public async Task<CurrentCanaryState?> GetCanaryState()
    {
        try
        {
            var result =
                await _clientProvider.GetClient().Get(
                    _canaryConfiguration.DynamoDataTable,
                    DynamoDbConstants.CanaryStateDocument
                );

            return ProcessCanaryStateResponse(result);
        }
        catch (ResourceNotFoundException exp)
        {
            return null;
        }
    }

    private CurrentCanaryState ProcessCanaryStateResponse(GetItemResponse result)
    {
        throw new NotImplementedException();
    }
}
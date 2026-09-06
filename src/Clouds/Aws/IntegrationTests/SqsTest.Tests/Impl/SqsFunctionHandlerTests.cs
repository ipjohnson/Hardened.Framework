using Hardened.Amz.Function.Sqs.Testing;
using Hardened.Shared.Testing.Attributes;
using SqsTest.Impl;
using Xunit;

namespace SqsTest.Tests.Impl;

public class SqsFunctionHandlerTests {
    [HardenedTest]
    public async Task SingleSend(TestSqsApp app, CountingService countingService) {
        var response = await app.SendMessage(new DataModel {
            IntValue = 20,
            Value = "Hello World"
        });
        
        Assert.NotNull(response);
        Assert.Empty(response.BatchItemFailures);
        Assert.Equal(1, countingService.Count);
    }
    
    [HardenedTest]
    public async Task MultipleSend(TestSqsApp app, CountingService countingService) {
        var response = await app.SendMessage(
        new DataModel {
            IntValue = 20,
            Value = "Hello World"
        },
        new DataModel {
            IntValue = 20,
            Value = "Hello World"
        },
        new DataModel {
            IntValue = 20,
            Value = "Hello World"
        });
        
        Assert.NotNull(response);
        Assert.Empty(response.BatchItemFailures);
        
        Assert.Equal(3, countingService.Count);
    }
}
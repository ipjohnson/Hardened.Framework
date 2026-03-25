using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation;

public class ValidationFilterTests {
    [Fact]
    public async Task ValidRequest_CallsChainNext() {
        var filter = new ValidationFilter(
            parameterRules: new[] {
                new ParameterValidationInfo("name",
                    new IValidationRule[] { new StringLengthRule(1, 100) })
            });

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var parameters = Substitute.For<IExecutionRequestParameters>();

        parameters.TryGetParameter("name", out Arg.Any<object?>())
            .Returns(x => { x[1] = "Buddy"; return true; });

        request.Parameters.Returns(parameters);
        context.Request.Returns(request);
        context.Response.Returns(response);
        chain.Context.Returns(context);

        await filter.Execute(chain);

        await chain.Received(1).Next();
    }

    [Fact]
    public async Task InvalidRequired_Returns400_NoChainNext() {
        var filter = new ValidationFilter(
            parameterRules: new[] {
                new ParameterValidationInfo("name",
                    new IValidationRule[] { RequiredRule.Instance })
            });

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var parameters = Substitute.For<IExecutionRequestParameters>();

        parameters.TryGetParameter("name", out Arg.Any<object?>())
            .Returns(x => { x[1] = null; return true; });

        request.Parameters.Returns(parameters);
        context.Request.Returns(request);
        context.Response.Returns(response);
        chain.Context.Returns(context);

        await filter.Execute(chain);

        response.Received(1).Status = 400;
        Assert.IsType<RequestValidationError>(response.ResponseValue);
        await chain.DidNotReceive().Next();
    }

    [Fact]
    public async Task MultipleErrors_Aggregated() {
        var filter = new ValidationFilter(
            parameterRules: new[] {
                new ParameterValidationInfo("name",
                    new IValidationRule[] { RequiredRule.Instance }),
                new ParameterValidationInfo("age",
                    new IValidationRule[] { new RangeRule(0m, 150m) })
            });

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var parameters = Substitute.For<IExecutionRequestParameters>();

        parameters.TryGetParameter("name", out Arg.Any<object?>())
            .Returns(x => { x[1] = null; return true; });
        parameters.TryGetParameter("age", out Arg.Any<object?>())
            .Returns(x => { x[1] = 200; return true; });

        request.Parameters.Returns(parameters);
        context.Request.Returns(request);
        context.Response.Returns(response);
        chain.Context.Returns(context);

        await filter.Execute(chain);

        response.Received(1).Status = 400;
        var errorModel = (RequestValidationError)response.ResponseValue!;
        Assert.Equal(2, errorModel.Errors.Count);
        await chain.DidNotReceive().Next();
    }

    [Fact]
    public async Task NoParameters_PassesThrough() {
        var filter = new ValidationFilter(
            parameterRules: Array.Empty<ParameterValidationInfo>());

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();

        request.Parameters.Returns((IExecutionRequestParameters?)null);
        context.Request.Returns(request);
        context.Response.Returns(response);
        chain.Context.Returns(context);

        await filter.Execute(chain);

        await chain.Received(1).Next();
    }

    [Fact]
    public async Task BodyPropertyValidation_Works() {
        var filter = new ValidationFilter(
            parameterRules: Array.Empty<ParameterValidationInfo>(),
            bodyPropertyRules: new[] {
                new PropertyValidationInfo("name",
                    new IValidationRule[] { RequiredRule.Instance },
                    body => ((TestBody)body).Name)
            },
            bodyParameterName: "body",
            requestBodyType: typeof(TestBody));

        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var parameters = Substitute.For<IExecutionRequestParameters>();
        var services = new ServiceCollection().BuildServiceProvider();

        parameters.TryGetParameter("body", out Arg.Any<object?>())
            .Returns(x => { x[1] = new TestBody { Name = null }; return true; });

        request.Parameters.Returns(parameters);
        context.Request.Returns(request);
        context.Response.Returns(response);
        context.RequestServices.Returns(services);
        chain.Context.Returns(context);

        await filter.Execute(chain);

        response.Received(1).Status = 400;
        await chain.DidNotReceive().Next();
    }

    private class TestBody {
        public string? Name { get; set; }
    }
}

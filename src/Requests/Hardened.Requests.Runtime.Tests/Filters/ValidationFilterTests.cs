using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SimpleFixture.NSubstitute;
using ValidationModules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Filters;

[SubFixtureInitialize]
public class ValidationFilterTests {

    [Fact]
    public async Task Execute_CallsNextWhenTheValueIsValid() {
        var called = 0;
        var chain = ChainFor(new Payload { Name = "ok" }, () => called++);

        await new ValidationFilter<Payload>(new[] { PayloadValidator.Instance }).Execute(chain);

        Assert.Equal(1, called);
    }

    [Fact]
    public async Task Execute_ThrowsValidationExceptionCarryingTheFailedFields() {
        var called = 0;
        var chain = ChainFor(new Payload { Name = "" }, () => called++);

        var exception = await Assert.ThrowsAsync<Hardened.Requests.Runtime.Validation.ValidationException>(
            () => new ValidationFilter<Payload>(new[] { PayloadValidator.Instance }).Execute(chain));

        Assert.Contains(exception.ValidationResult.Errors, error => error.Field == "name");
        Assert.Equal(0, called);
    }

    /// <summary>
    /// Every validator registered for the type runs into one collector, so a hand-written one adds
    /// to the generated checks rather than replacing them.
    /// </summary>
    [Fact]
    public async Task Execute_MergesTheResultsOfEveryValidator() {
        var chain = ChainFor(new Payload { Name = "" }, () => { });

        var exception = await Assert.ThrowsAsync<Hardened.Requests.Runtime.Validation.ValidationException>(
            () => new ValidationFilter<Payload>(
                new IValidatorFor<Payload>[] { PayloadValidator.Instance, SecondPayloadValidator.Instance })
                .Execute(chain));

        Assert.Contains(exception.ValidationResult.Errors, error => error.Field == "name");
        Assert.Contains(exception.ValidationResult.Errors, error => error.Field == "second");
    }

    /// <summary>
    /// Parameters that are not the validated shape are a defect, not a case to skip.
    /// </summary>
    /// <remarks>
    /// This used to call <c>chain.Next()</c> and return. The request was then answered normally
    /// with nothing validated, which is indistinguishable from a request that passed - the silent
    /// non-validation this design refuses everywhere else. The filter is only in the chain because
    /// something declared constraints, so reaching here means whoever attached it and whoever bound
    /// the parameters disagree.
    /// </remarks>
    [Fact]
    public async Task Execute_ThrowsWhenTheParametersAreNotTheValidatedType() {
        var called = 0;
        var chain = ChainFor(new UnrelatedParameters(), () => called++);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ValidationFilter<Payload>(new[] { PayloadValidator.Instance }).Execute(chain));

        Assert.Contains(nameof(UnrelatedParameters), exception.Message);
        Assert.Equal(0, called);
    }

    [Fact]
    public async Task Execute_ThrowsWhenThereAreNoParametersAtAll() {
        var chain = ChainFor(null, () => { });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ValidationFilter<Payload>(new[] { PayloadValidator.Instance }).Execute(chain));
    }

    private static IExecutionChain ChainFor(IExecutionRequestParameters? parameters, Action onNext) {
        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Parameters.Returns(parameters);
        context.Request.Returns(request);
        context.RequestServices.Returns(new ServiceCollection().BuildServiceProvider());
        chain.Context.Returns(context);

        chain.Next().Returns(_ => {
            onNext();

            return Task.CompletedTask;
        });

        return chain;
    }

    /// <summary>
    /// Stands in for a generated <c>Parameters</c> class: an
    /// <see cref="IExecutionRequestParameters"/> that a validator is typed on.
    /// </summary>
    public class Payload : TestParameters {
        public string Name { get; set; } = "";
    }

    public class UnrelatedParameters : TestParameters;

    public sealed class PayloadValidator : IValidatorFor<Payload> {
        public static readonly PayloadValidator Instance = new();

        private PayloadValidator() { }

        public ValidationFlow Validate(ref ValidationContext ctx, Payload value) {
            if (string.IsNullOrEmpty(value.Name)) {
                return ctx.ReportRequired("name");
            }

            return ValidationFlow.Continue;
        }
    }

    public sealed class SecondPayloadValidator : IValidatorFor<Payload> {
        public static readonly SecondPayloadValidator Instance = new();

        private SecondPayloadValidator() { }

        public ValidationFlow Validate(ref ValidationContext ctx, Payload value) => ctx.ReportRequired("second");
    }

    public abstract class TestParameters : IExecutionRequestParameters {
        public object this[int index] {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public IReadOnlyList<IExecutionRequestParameter> Info => Array.Empty<IExecutionRequestParameter>();

        public int ParameterCount => 0;

        public IExecutionRequestParameters Clone() => this;

        public bool TryGetParameter(string name, out object? value) {
            value = null;

            return false;
        }

        public bool TrySetParameter(string name, object? value) => false;
    }
}

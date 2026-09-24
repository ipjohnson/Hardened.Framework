using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Tests.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ValidationModules;
using Xunit;
using Payload = Hardened.Requests.Runtime.Tests.Filters.ValidationFilterTests.Payload;
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

namespace Hardened.Requests.Runtime.Tests.Validation;

/// <summary>
/// <see cref="ValidationModeAttribute"/>: whether a handler's validation reports every failed rule
/// or stops at the first.
/// </summary>
/// <remarks>
/// The module rung is in <c>ApplicationFilterRungTests</c>, beside the merge it depends on.
/// </remarks>
public class ValidationModeTests
{
    private static readonly IValidatorFor<Payload>[] Both =
    [
        ValidationFilterTests.PayloadValidator.Instance,
        ValidationFilterTests.SecondPayloadValidator.Instance,
    ];

    [Fact]
    public async Task StoppingAtTheFirstErrorReportsOnlyTheFirstFailure()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new ValidationFilter<Payload>(Both, ValidationStopMode.StopOnFirstError).Execute(
                ChainFor(new Payload { Name = "" })
            )
        );

        Assert.Equal("name", Assert.Single(exception.ValidationResult.Errors).Field);
    }

    [Fact]
    public async Task CollectingEveryFailureIsTheDefault()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new ValidationFilter<Payload>(Both).Execute(ChainFor(new Payload { Name = "" }))
        );

        Assert.Equal(2, exception.ValidationResult.Errors.Count);
    }

    /// <summary>
    /// A warning is advisory and does not stop a pass, so an error a later validator reports is
    /// still found.
    /// </summary>
    [Fact]
    public async Task AWarningDoesNotStopThePass()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new ValidationFilter<Payload>(
                [WarningValidator.Instance, ValidationFilterTests.SecondPayloadValidator.Instance],
                ValidationStopMode.StopOnFirstError
            ).Execute(ChainFor(new Payload { Name = "ok" }))
        );

        Assert.Contains(exception.ValidationResult.Errors, error => error.Field == "second");
    }

    [Fact]
    public async Task AValueThatPassesReachesTheHandler()
    {
        var called = 0;

        await new ValidationFilter<Payload>(
            [ValidationFilterTests.PayloadValidator.Instance],
            ValidationStopMode.StopOnFirstError
        ).Execute(ChainFor(new Payload { Name = "ok" }, () => called++));

        Assert.Equal(1, called);
    }

    /// <summary>
    /// The metadata lists a method's declarations before its class's, so the first is the nearest.
    /// </summary>
    [Fact]
    public void TheFirstDeclarationInTheMetadataWins()
    {
        var handler = Info(
            new ValidationModeAttribute(ValidationStopMode.StopOnFirstError),
            new ValidationModeAttribute(ValidationStopMode.CollectAll)
        );

        Assert.Equal(ValidationStopMode.StopOnFirstError, ValidationModeAttribute.For(handler));
    }

    [Fact]
    public void AHandlerDeclaringNothingCollectsEveryFailure()
    {
        Assert.Equal(ValidationStopMode.CollectAll, ValidationModeAttribute.For(Info()));
    }

    [Fact]
    public void TheDeclarationContributesNoFilterOfItsOwn()
    {
        Assert.Empty(
            new ValidationModeAttribute(ValidationStopMode.StopOnFirstError).GetFilters(Info())
        );
    }

    /// <summary>
    /// The provider a generator emits for a code-first handler reads the declaration from the
    /// handler it is installed on.
    /// </summary>
    [Fact]
    public async Task TheGeneratedProviderBuildsItsFilterInTheDeclaredMode()
    {
        var info = Assert.Single(
            new ValidationFilterProvider<Payload>().GetFilters(
                Info(new ValidationModeAttribute(ValidationStopMode.StopOnFirstError))
            )
        );

        await AssertStopsAtTheFirst(info.FilterFunc(ContextWith(Both)));
    }

    /// <summary>And so does the attribute a contract-first operation carries.</summary>
    [Fact]
    public async Task TheDescribedAttributeBuildsItsFilterInTheDeclaredMode()
    {
        var info = Assert.Single(
            new ValidateAttribute<Payload>().GetFilters(
                Info(new ValidationModeAttribute(ValidationStopMode.StopOnFirstError))
            )
        );

        await AssertStopsAtTheFirst(info.FilterFunc(ContextWith(Both)));
    }

    private static async Task AssertStopsAtTheFirst(IExecutionFilter filter)
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            filter.Execute(ChainFor(new Payload { Name = "" }))
        );

        Assert.Single(exception.ValidationResult.Errors);
    }

    private static IExecutionRequestHandlerInfo Info(params object[] metadata) =>
        new ExecutionRequestHandlerInfo(
            "/orders",
            "POST",
            typeof(ValidationModeTests),
            nameof(Info),
            metadata: metadata
        );

    private static IExecutionContext ContextWith(IValidatorFor<Payload>[] validators) =>
        Pipeline.Context(configureServices: services =>
        {
            foreach (var validator in validators)
            {
                services.AddSingleton(validator);
            }
        });

    private static IExecutionChain ChainFor(Payload parameters, Action? onNext = null)
    {
        var chain = Substitute.For<IExecutionChain>();
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();

        request.Parameters.Returns(parameters);
        context.Request.Returns(request);
        context.RequestServices.Returns(new ServiceCollection().BuildServiceProvider());
        chain.Context.Returns(context);

        chain
            .Next()
            .Returns(_ =>
            {
                onNext?.Invoke();

                return Task.CompletedTask;
            });

        return chain;
    }

    private sealed class WarningValidator : IValidatorFor<Payload>
    {
        public static readonly WarningValidator Instance = new();

        private WarningValidator() { }

        public ValidationFlow Validate(ref ValidationContext ctx, Payload value) =>
            ctx.Report("hint", "hint", "Worth a second look.", ValidationSeverity.Warning);
    }
}

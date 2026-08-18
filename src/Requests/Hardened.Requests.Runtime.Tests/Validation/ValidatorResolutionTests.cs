using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Tests.Filters;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ValidationModules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation;

/// <summary>
/// How a validation filter gets its validators.
/// </summary>
/// <remarks>
/// <para>
/// <c>ValidationFilterTests</c> covers the filter once it has them. This covers the two types that
/// find them — <see cref="ValidateAttribute{T}"/>, which an author writes by hand, and
/// <see cref="ValidationFilterProvider{T}"/>, which a generator emits against a handler's nested
/// <c>Parameters</c> class. Both sat at 55% line / 50% branch, and the untaken branch in each was
/// the throw.
/// </para>
/// <para>
/// <b>The throw is the behaviour worth having a test for.</b> Both classes carry a paragraph
/// explaining that an empty validator set must fail loudly rather than read as "nothing to check",
/// because the filter is only ever attached when something declared constraints — so an empty set
/// means the application was wired against a different entry point, and passing silently would turn
/// validation off on a build that otherwise looks fine. Neither had a test proving it does.
/// </para>
/// </remarks>
public class ValidatorResolutionTests {

    private static IExecutionRequestHandlerInfo HandlerInfo() =>
        Substitute.For<IExecutionRequestHandlerInfo>();

    private static IExecutionContext ContextWith(params IValidatorFor<ValidationFilterTests.Payload>[] validators) =>
        Pipeline.Context(configureServices: services => {
            foreach (var validator in validators) {
                services.AddSingleton(validator);
            }
        });

    #region ValidateAttribute

    [Fact]
    public void TheAttributeYieldsOneFilterAtTheValidationPosition() {
        var info = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        Assert.Equal(FilterOrder.Validation, info.Order);
    }

    [Fact]
    public void TheAttributeBuildsAValidationFilterForItsType() {
        var info = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var filter = info.FilterFunc(ContextWith(ValidationFilterTests.PayloadValidator.Instance));

        Assert.IsType<ValidationFilter<ValidationFilterTests.Payload>>(filter);
    }

    /// <summary>
    /// An empty set is a wiring fault, not an absence of work.
    /// </summary>
    [Fact]
    public void TheAttributeThrowsWhenNoValidatorIsRegistered() {
        var info = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var exception =
            Assert.Throws<InvalidOperationException>(() => info.FilterFunc(ContextWith()));

        Assert.Contains(nameof(ValidationFilterTests.Payload), exception.Message);
        Assert.Contains("entry point", exception.Message);
    }

    /// <summary>
    /// <c>GetFilters</c> runs from the handler's constructor, which has no service provider, so the
    /// filter is built on the first request and kept. Rebuilding it would put a container lookup on
    /// the steady-state request path of every validated handler.
    /// </summary>
    [Fact]
    public void TheAttributeBuildsItsFilterOnceAcrossRequests() {
        var info = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var first = info.FilterFunc(ContextWith(ValidationFilterTests.PayloadValidator.Instance));
        var second = info.FilterFunc(ContextWith(ValidationFilterTests.PayloadValidator.Instance));

        Assert.Same(first, second);
    }

    /// <summary>
    /// Two attributes are two filters, and they do not share a cached instance.
    /// </summary>
    [Fact]
    public void TwoAttributesBuildTheirOwnFilters() {
        var context = ContextWith(ValidationFilterTests.PayloadValidator.Instance);

        var first = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));
        var second = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        Assert.NotSame(first.FilterFunc(context), second.FilterFunc(context));
    }

    #endregion

    #region ValidationFilterProvider

    [Fact]
    public void TheProviderYieldsOneFilterAtTheValidationPosition() {
        var info = Assert.Single(
            new ValidationFilterProvider<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        Assert.Equal(FilterOrder.Validation, info.Order);
    }

    [Fact]
    public void TheProviderBuildsAValidationFilterForItsType() {
        var info = Assert.Single(
            new ValidationFilterProvider<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var filter = info.FilterFunc(ContextWith(ValidationFilterTests.PayloadValidator.Instance));

        Assert.IsType<ValidationFilter<ValidationFilterTests.Payload>>(filter);
    }

    /// <summary>
    /// The generated route into the same failure. A generator emits the provider alongside a
    /// validator registration in one run, so an empty set means the application was built against
    /// a different entry point.
    /// </summary>
    [Fact]
    public void TheProviderThrowsWhenNoValidatorIsRegistered() {
        var info = Assert.Single(
            new ValidationFilterProvider<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var exception =
            Assert.Throws<InvalidOperationException>(() => info.FilterFunc(ContextWith()));

        Assert.Contains(nameof(ValidationFilterTests.Payload), exception.Message);
    }

    [Fact]
    public void TheProviderBuildsItsFilterOnceAcrossRequests() {
        var info = Assert.Single(
            new ValidationFilterProvider<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var first = info.FilterFunc(ContextWith(ValidationFilterTests.PayloadValidator.Instance));
        var second = info.FilterFunc(ContextWith(ValidationFilterTests.PayloadValidator.Instance));

        Assert.Same(first, second);
    }

    #endregion

    /// <summary>
    /// Every registered validator reaches the filter, so a hand-written one runs alongside the
    /// generated one rather than one replacing the other.
    /// </summary>
    [Fact]
    public async Task EveryRegisteredValidatorReachesTheFilter() {
        var info = Assert.Single(
            new ValidateAttribute<ValidationFilterTests.Payload>().GetFilters(HandlerInfo()));

        var context = ContextWith(
            ValidationFilterTests.PayloadValidator.Instance,
            ValidationFilterTests.SecondPayloadValidator.Instance);

        context.Request.Parameters = new ValidationFilterTests.Payload { Name = "" };

        var exception = await Assert.ThrowsAsync<Hardened.Requests.Runtime.Validation.ValidationException>(
            () => Pipeline.Chain(context, info.FilterFunc(context)).Next());

        Assert.Contains(exception.ValidationResult.Errors, error => error.Field == "name");
        Assert.Contains(exception.ValidationResult.Errors, error => error.Field == "second");
    }
}

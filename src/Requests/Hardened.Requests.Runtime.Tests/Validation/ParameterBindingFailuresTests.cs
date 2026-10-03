using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;
using NSubstitute;
using ValidationModules;
using Xunit;
using static Hardened.Requests.Runtime.Tests.Filters.ValidationFilterTests;
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

namespace Hardened.Requests.Runtime.Tests.Validation;

public class ParameterBindingFailuresTests
{
    [Fact]
    public void NothingIsThrownWhenEveryParameterBound()
    {
        var failures = new ParameterBindingFailures();

        failures.ThrowIfAny(new Payload());
    }

    [Fact]
    public void EveryFailureIsReportedInTheOrderItWasRecorded()
    {
        var parameters = new Payload();
        var failures = new ParameterBindingFailures();

        failures.Add(Failure("status", "invalid"));
        failures.Add(Failure("pageSize", "invalid"));

        var exception = Assert.Throws<ParameterBindingException>(() =>
            failures.ThrowIfAny(parameters)
        );

        Assert.Equal(
            new[] { "status", "pageSize" },
            exception.ValidationResult.Errors.Select(error => error.Field)
        );
        Assert.Same(parameters, exception.Parameters);
    }

    /// <summary>
    /// The constraints over what bound are reported after the binding failures, and a constraint
    /// on a field that failed to bind is dropped: it would describe the default, not what was sent.
    /// </summary>
    [Fact]
    public void TheConstraintsOnTheParametersThatBoundAreReportedBeside()
    {
        var filter = new ValidationFilter<Payload>(
            new IValidatorFor<Payload>[]
            {
                PayloadValidator.Instance,
                SecondPayloadValidator.Instance,
            }
        );

        var result = Merge(filter, Failure("name", "invalid"));

        Assert.Equal(
            new[] { ("name", "invalid"), ("second", "required") },
            result.ValidationResult.Errors.Select(error => (error.Field, error.Code))
        );
    }

    [Fact]
    public void ConstraintsOnlyOnFieldsThatFailedLeaveTheFailureAsItWas()
    {
        var filter = new ValidationFilter<Payload>(new[] { PayloadValidator.Instance });

        var result = Merge(filter, Failure("name", "invalid"));

        Assert.Equal(
            new[] { ("name", "invalid") },
            result.ValidationResult.Errors.Select(error => (error.Field, error.Code))
        );
    }

    [Fact]
    public void AConstraintUnderAFieldThatFailedIsDropped()
    {
        var filter = new ValidationFilter<Payload>(new[] { NestedValidator.Instance });

        var result = Merge(filter, Failure("filter", "invalid"));

        Assert.Equal(
            new[] { "filter", "filterless" },
            result.ValidationResult.Errors.Select(error => error.Field)
        );
    }

    [Fact]
    public void AValidatorThatThrowsLeavesTheBindingFailuresAlone()
    {
        var filter = new ValidationFilter<Payload>(new[] { ThrowingValidator.Instance });

        var result = Merge(filter, Failure("status", "invalid"));

        Assert.Equal("status", Assert.Single(result.ValidationResult.Errors).Field);
    }

    [Fact]
    public void StopOnFirstErrorAddsNothing()
    {
        var filter = new ValidationFilter<Payload>(
            new IValidatorFor<Payload>[] { SecondPayloadValidator.Instance },
            ValidationStopMode.StopOnFirstError
        );

        var result = Merge(filter, Failure("status", "invalid"));

        Assert.Equal("status", Assert.Single(result.ValidationResult.Errors).Field);
    }

    [Fact]
    public void ABodyFailureHandsBackWhatWasRead()
    {
        var body = new Payload();
        var failures = new ParameterBindingFailures();

        Assert.Same(
            body,
            failures.AddBody<Payload>(BodyFailure("body.color", body: body, field: "body"))
        );
        Assert.Null(failures.AddBody<Payload>(Failure("body", "required")));
    }

    /// <summary>
    /// A body that could not be read to the end is all defaults, so no constraint under it
    /// describes what was sent.
    /// </summary>
    [Fact]
    public void AConstraintUnderABodyThatWasNotReadIsDropped()
    {
        var filter = new ValidationFilter<Payload>(new[] { NestedValidator.Instance });

        var result = Merge(filter, BodyFailure("filter.color", body: null, field: "filter"));

        Assert.Equal(
            new[] { "filter.color", "filterless" },
            result.ValidationResult.Errors.Select(error => error.Field)
        );
    }

    private static BodyBindingException BodyFailure(string error, object? body, string field) =>
        new(
            ValidationResult.FromErrors(new[] { new ValidationError(error, "invalid", error) }),
            field,
            body,
            new InvalidOperationException()
        );

    private static ValidationException Merge(IExecutionFilter filter, ValidationException failure)
    {
        var failures = new ParameterBindingFailures();

        failures.Add(failure);

        var binding = Assert.Throws<ParameterBindingException>(() =>
            failures.ThrowIfAny(new Payload())
        );

        var answered = BindFailure.For(
            binding,
            Substitute.For<IExecutionContext>(),
            new Func<IExecutionContext, IExecutionFilter>[] { _ => filter }
        );

        return Assert.IsAssignableFrom<ValidationException>(answered);
    }

    private static ValidationException Failure(string field, string code) =>
        new(ValidationResult.FromErrors(new[] { new ValidationError(field, code, field) }));

    private sealed class NestedValidator : IValidatorFor<Payload>
    {
        public static readonly NestedValidator Instance = new();

        public ValidationFlow Validate(ref ValidationContext ctx, Payload value)
        {
            ctx.ReportRequired("filter.status");
            ctx.ReportRequired("filter[0]");

            return ctx.ReportRequired("filterless");
        }
    }

    private sealed class ThrowingValidator : IValidatorFor<Payload>
    {
        public static readonly ThrowingValidator Instance = new();

        public ValidationFlow Validate(ref ValidationContext ctx, Payload value) =>
            throw new NullReferenceException();
    }
}

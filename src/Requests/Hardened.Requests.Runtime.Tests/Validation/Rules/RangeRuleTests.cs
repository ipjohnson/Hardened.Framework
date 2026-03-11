using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation.Rules;

public class RangeRuleTests {
    [Fact]
    public void BelowMinimum_AddsError() {
        var rule = new RangeRule(5m, null);
        var result = new ValidationResult();
        rule.Validate("age", 3, result);
        Assert.False(result.IsValid);
        Assert.Contains("at least", result.Errors[0].Message);
    }

    [Fact]
    public void AboveMaximum_AddsError() {
        var rule = new RangeRule(null, 100m);
        var result = new ValidationResult();
        rule.Validate("age", 101, result);
        Assert.False(result.IsValid);
        Assert.Contains("at most", result.Errors[0].Message);
    }

    [Fact]
    public void ExclusiveMinimum_EqualValue_AddsError() {
        var rule = new RangeRule(0m, null, exclusiveMinimum: true);
        var result = new ValidationResult();
        rule.Validate("price", 0, result);
        Assert.False(result.IsValid);
        Assert.Contains("greater than", result.Errors[0].Message);
    }

    [Fact]
    public void ExclusiveMaximum_EqualValue_AddsError() {
        var rule = new RangeRule(null, 100m, exclusiveMaximum: true);
        var result = new ValidationResult();
        rule.Validate("score", 100, result);
        Assert.False(result.IsValid);
        Assert.Contains("less than", result.Errors[0].Message);
    }

    [Fact]
    public void WithinRange_NoError() {
        var rule = new RangeRule(0m, 100m);
        var result = new ValidationResult();
        rule.Validate("age", 50, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullValue_Skipped() {
        var rule = new RangeRule(0m, 100m);
        var result = new ValidationResult();
        rule.Validate("age", null, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void AtMinBoundary_Inclusive_NoError() {
        var rule = new RangeRule(0m, null);
        var result = new ValidationResult();
        rule.Validate("age", 0, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void AtMaxBoundary_Inclusive_NoError() {
        var rule = new RangeRule(null, 100m);
        var result = new ValidationResult();
        rule.Validate("age", 100, result);
        Assert.True(result.IsValid);
    }
}

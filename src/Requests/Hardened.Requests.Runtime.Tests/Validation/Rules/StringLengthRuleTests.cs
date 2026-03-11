using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation.Rules;

public class StringLengthRuleTests {
    [Fact]
    public void BelowMinLength_AddsError() {
        var rule = new StringLengthRule(3, null);
        var result = new ValidationResult();
        rule.Validate("name", "ab", result);
        Assert.False(result.IsValid);
        Assert.Contains("at least 3", result.Errors[0].Message);
    }

    [Fact]
    public void AboveMaxLength_AddsError() {
        var rule = new StringLengthRule(null, 5);
        var result = new ValidationResult();
        rule.Validate("name", "abcdef", result);
        Assert.False(result.IsValid);
        Assert.Contains("at most 5", result.Errors[0].Message);
    }

    [Fact]
    public void WithinBounds_NoError() {
        var rule = new StringLengthRule(2, 10);
        var result = new ValidationResult();
        rule.Validate("name", "hello", result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ExactMinLength_NoError() {
        var rule = new StringLengthRule(3, null);
        var result = new ValidationResult();
        rule.Validate("name", "abc", result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullValue_Skipped() {
        var rule = new StringLengthRule(1, 10);
        var result = new ValidationResult();
        rule.Validate("name", null, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonStringValue_Skipped() {
        var rule = new StringLengthRule(1, 10);
        var result = new ValidationResult();
        rule.Validate("name", 42, result);
        Assert.True(result.IsValid);
    }
}

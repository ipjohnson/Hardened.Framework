using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation.Rules;

public class RequiredRuleTests {
    [Fact]
    public void Null_AddsError() {
        var result = new ValidationResult();
        RequiredRule.Instance.Validate("name", null, result);
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("required", result.Errors[0].Code);
    }

    [Fact]
    public void EmptyString_AddsError() {
        var result = new ValidationResult();
        RequiredRule.Instance.Validate("name", "", result);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void WhitespaceString_AddsError() {
        var result = new ValidationResult();
        RequiredRule.Instance.Validate("name", "   ", result);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonNullValue_NoError() {
        var result = new ValidationResult();
        RequiredRule.Instance.Validate("name", "hello", result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonNullObject_NoError() {
        var result = new ValidationResult();
        RequiredRule.Instance.Validate("count", 42, result);
        Assert.True(result.IsValid);
    }
}

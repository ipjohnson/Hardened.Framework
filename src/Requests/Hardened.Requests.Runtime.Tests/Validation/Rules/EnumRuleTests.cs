using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation.Rules;

public class EnumRuleTests {
    [Fact]
    public void InvalidValue_AddsError() {
        var rule = new EnumRule(new[] { "available", "pending", "sold" });
        var result = new ValidationResult();
        rule.Validate("status", "unknown", result);
        Assert.False(result.IsValid);
        Assert.Contains("enum", result.Errors[0].Code);
    }

    [Fact]
    public void ValidValue_NoError() {
        var rule = new EnumRule(new[] { "available", "pending", "sold" });
        var result = new ValidationResult();
        rule.Validate("status", "available", result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CaseInsensitive_NoError() {
        var rule = new EnumRule(new[] { "available", "pending", "sold" });
        var result = new ValidationResult();
        rule.Validate("status", "Available", result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullValue_Skipped() {
        var rule = new EnumRule(new[] { "available", "pending" });
        var result = new ValidationResult();
        rule.Validate("status", null, result);
        Assert.True(result.IsValid);
    }
}

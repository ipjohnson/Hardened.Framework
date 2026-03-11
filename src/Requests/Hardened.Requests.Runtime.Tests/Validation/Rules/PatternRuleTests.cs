using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Validation.Rules;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Validation.Rules;

public class PatternRuleTests {
    [Fact]
    public void NonMatchingPattern_AddsError() {
        var rule = new PatternRule("^[a-z]+$");
        var result = new ValidationResult();
        rule.Validate("tag", "ABC123", result);
        Assert.False(result.IsValid);
        Assert.Contains("pattern", result.Errors[0].Code);
    }

    [Fact]
    public void MatchingPattern_NoError() {
        var rule = new PatternRule("^[a-z]+$");
        var result = new ValidationResult();
        rule.Validate("tag", "abc", result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullValue_Skipped() {
        var rule = new PatternRule("^[a-z]+$");
        var result = new ValidationResult();
        rule.Validate("tag", null, result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonStringValue_Skipped() {
        var rule = new PatternRule("^[a-z]+$");
        var result = new ValidationResult();
        rule.Validate("tag", 42, result);
        Assert.True(result.IsValid);
    }
}

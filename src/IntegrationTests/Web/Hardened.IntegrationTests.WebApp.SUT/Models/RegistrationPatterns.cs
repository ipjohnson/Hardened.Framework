using System.Text.RegularExpressions;

namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>
/// A pattern that backtracks catastrophically on a run of a's followed by anything else, under a
/// timeout short enough for a test to reach.
/// </summary>
public static partial class RegistrationPatterns
{
    [GeneratedRegex("^(a+)+$", RegexOptions.None, matchTimeoutMilliseconds: 50)]
    public static partial Regex Nested();
}

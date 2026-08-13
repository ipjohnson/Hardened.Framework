namespace Hardened.Templates.RazorBlade.Tests.Models;

public record Fortune(int Id, string Message);

public record FortunePage(List<Fortune> Fortunes);

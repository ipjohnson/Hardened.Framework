namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>One member of staff, listed newest first.</summary>
public record StaffMember(int Id, string Name, DateTimeOffset CreatedAt);

/// <summary>Where a page of staff ended, in the order they are listed.</summary>
public record StaffCursor(DateTimeOffset CreatedAt, int Id);

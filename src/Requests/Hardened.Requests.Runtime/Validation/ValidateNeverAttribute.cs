namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Binds a parameter, or every parameter of a handler, without validating it.
/// </summary>
/// <remarks>
/// <para>
/// A type that declares constraints is otherwise validated on every route that binds it. A route
/// that echoes what it was sent, or checks the values itself, would need a second type of the same
/// shape without the constraints. ASP.NET Core gives the same switch the same name.
/// </para>
/// <para>
/// Read when the handler is generated, and the handler's validator leaves the parameter out, or is
/// not generated at all. Binding still refuses a value that does not convert to the parameter's
/// type and a body that does not deserialize. A specification-first handler validates what its
/// contract declares, and ignores this.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter)]
public sealed class ValidateNeverAttribute : Attribute { }

namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// The <c>operationId</c> this handler publishes, when the method name is not the right answer.
///
/// <para>
/// The default is the method name in camelCase, so <c>ListTodos</c> publishes <c>listTodos</c>,
/// and where two controllers share a method name the tag is prefixed to tell them apart. The id
/// is what a generated client names its method after - a Refit interface's <c>ListTodos()</c> is
/// the operation <c>listTodos</c> - so it is part of the contract, and deriving it from the method
/// name makes a rename in C# a breaking change on the wire. Declaring it holds it still.
/// </para>
///
/// <para>
/// Use it where the method's name and the operation's public name differ - a code-first service
/// whose document has to match a contract written elsewhere, or a handler named for an internal
/// concept - and wherever the id has to survive a refactor. It is written to the document as
/// given. Two handlers declaring one id is an error, <c>HRDOA004</c>: the ids in a document name
/// one operation each.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class OperationAttribute : Attribute {
    public OperationAttribute(string id) {
        Id = id;
    }

    public string Id { get; }
}

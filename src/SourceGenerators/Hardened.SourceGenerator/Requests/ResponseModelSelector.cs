using System;
using System.Linq;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The response model an entry point declared, from <c>[ResponseModel(...)]</c> on it.
/// </summary>
/// <remarks>
/// <para>
/// Read the same way <c>[BasePath]</c> and <c>[CaseInsensitiveRoutes]</c> are - off
/// <c>EntryPointSelector.Model.AttributeModels</c>, by name prefix - because a module attribute has
/// one place it can be written and this is already the code that goes and looks there.
/// </para>
/// <para>
/// <b>Derived rather than stored on the model.</b> Putting a <c>ResponseModel</c> field on
/// <c>EntryPointSelector.Model</c> would mean adding it to that type's equality, and a model that
/// is a Roslyn cache key which compares equal while carrying a changed value is the trap
/// <c>ResponseInformationModel.ToString()</c> has already been caught by twice. The attribute list
/// is on the model and is already compared; projecting from it is free and cannot go stale.
/// </para>
/// <para>
/// It also does not touch <c>CompilationProvider</c>. Nothing here needs the compilation, and
/// reaching for one is what <c>EnabledFeatureSelector</c>'s own remarks warn costs incrementality.
/// </para>
/// </remarks>
public static class ResponseModelSelector {

    /// <summary>The attribute's name, without the <c>Attribute</c> suffix a user may or may not write.</summary>
    private const string AttributeName = "ResponseModel";

    /// <summary>
    /// What an entry point that says nothing gets, which is what every application built before
    /// this existed says.
    /// </summary>
    public const ResponseModelValue Default = ResponseModelValue.Standard;

    /// <summary>
    /// The declared model, or <see cref="Default"/>.
    /// </summary>
    /// <remarks>
    /// An unrecognised argument also yields the default rather than throwing. The enum is the only
    /// thing a caller can write and the compiler already rejects anything else, so a value that
    /// does not parse here means the attribute was written against a newer Hardened than this
    /// generator - and emitting standard-mode code is the answer that still builds.
    /// </remarks>
    public static ResponseModelValue Read(EntryPointSelector.Model appModel) {
        if (appModel.AttributeModels == null) {
            return Default;
        }

        var attribute = appModel.AttributeModels.FirstOrDefault(model =>
            model.TypeDefinition.Name.StartsWith(AttributeName, StringComparison.Ordinal));

        if (attribute == null) {
            return Default;
        }

        return Parse(attribute.Arguments);
    }

    /// <summary>
    /// Whether the entry point said anything at all, as opposed to saying <c>Standard</c>.
    /// </summary>
    /// <remarks>
    /// The two are the same emit and a different diagnostic. An entry point that wrote
    /// <c>[ResponseModel(ResponseModel.Standard)]</c> has made a choice and should not be told
    /// anything; one that wrote nothing has not, and is not the subject of any message either. Kept
    /// separate so a later mode-mismatch diagnostic can tell them apart without re-reading.
    /// </remarks>
    public static bool IsDeclared(EntryPointSelector.Model appModel) =>
        appModel.AttributeModels != null &&
        appModel.AttributeModels.Any(model =>
            model.TypeDefinition.Name.StartsWith(AttributeName, StringComparison.Ordinal));

    /// <summary>
    /// The enum member named by the attribute's argument text.
    /// </summary>
    /// <remarks>
    /// The arguments arrive as source text with names qualified, so the value is
    /// <c>Hardened.Requests.Abstract.Responses.ResponseModel.Union</c> rather than <c>Union</c> -
    /// and an unqualified <c>ResponseModel.Union</c> or a bare <c>Union</c> are both legal to write.
    /// Taking the last dotted segment covers all three without the generator having to resolve a
    /// symbol for something the compiler has already type-checked.
    /// </remarks>
    private static ResponseModelValue Parse(string arguments) {
        if (string.IsNullOrEmpty(arguments)) {
            return Default;
        }

        var first = arguments.Split(',')[0].Trim();
        var member = first.Substring(first.LastIndexOf('.') + 1).Trim();

        switch (member) {
            case nameof(ResponseModelValue.Response):
                return ResponseModelValue.Response;
            case nameof(ResponseModelValue.Union):
                return ResponseModelValue.Union;
            default:
                return ResponseModelValue.Standard;
        }
    }
}

/// <summary>
/// The generator's own copy of <c>Hardened.Requests.Abstract.Responses.ResponseModel</c>.
/// </summary>
/// <remarks>
/// A separate type because this assembly targets <c>netstandard2.0</c> and references no Hardened
/// runtime package - the generator reads the attribute as source text and never loads the type that
/// declares it. The names must agree with the public enum, which
/// <c>ResponseModelSelectorTests</c> asserts rather than assumes.
/// </remarks>
public enum ResponseModelValue {
    Standard,
    Response,
    Union
}

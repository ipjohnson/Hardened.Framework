using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl;
using Hardened.Idl.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// A type holding exactly one of the schemas a <c>oneOf</c> names.
/// </summary>
/// <remarks>
/// <para>
/// The alternative was to type the property <c>JsonElement</c> and let the caller take it apart,
/// which is what happened before this: the payload arrived as unparsed JSON, the branch types were
/// reachable from nothing so they were not generated at all, and the caller who wanted a <c>Cat</c>
/// had neither the type nor a way to know it was one. Here the converter resolves the branch while
/// reading, so <c>Value</c> is already a <c>Cat</c> and <c>switch (payload.Value)</c> binds it.
/// </para>
/// <para>
/// A struct, so an optional payload is <c>T?</c> and a required one cannot be null - the same shape
/// a generated enum already has, which is why the type mapper needs one predicate for both. There is
/// one constructor per branch and they are the only way in, so a value the schema does not permit
/// does not compile, and the only instance that can hold nothing is <c>default</c>, which nothing
/// here produces. It would be a <c>readonly struct</c> if CSharpAuthor rendered that modifier on a
/// type; the single get-only property makes it immutable either way.
/// </para>
/// <para>
/// Named for where it is declared rather than for what it holds - <c>OneOfHolderPayload</c> rather
/// than <c>CatOrDog</c> - so adding a branch does not rename the type, and a document with fifteen
/// of them does not produce a name with fifteen words in it.
/// </para>
/// <para>
/// <b>C# 15 declares this natively</b> - <c>public union OneOfHolderPayload(Cat, Dog);</c> - and it
/// is the same design: cases composed from existing types, implicit conversions from each, and the
/// contents in a public <c>Value</c> of type <c>object?</c>. What the language adds is what a
/// generator cannot: <c>switch (payload)</c> unwraps without naming <c>Value</c>, and a switch
/// covering every case is checked for exhaustiveness.
/// </para>
/// <para>
/// Not emitted here because generated code compiles in the consumer's project, and the keyword needs
/// a <b>C# 15 compiler</b> - which is a <c>LangVersion</c> question, not a target framework one. A
/// <c>net8.0</c> project on the .NET 11 SDK can declare a union; a <c>net11.0</c> project on an
/// older SDK cannot, so gating this on the target framework would answer both wrongly. When it is
/// emitted it becomes a second emit selected by the module, alongside this one rather than instead
/// of it.
/// </para>
/// <para>
/// The swap costs a caller nothing they wrote deliberately: <c>Value</c> is spelled the same either
/// way, so <c>switch (payload.Value) { case Cat c: ... }</c> keeps compiling. What does change is a
/// pattern applied to the wrapper itself - <c>payload is { Value: Cat c }</c> - because patterns on
/// a union unwrap to <c>Value</c>, so the property pattern starts looking for a <c>Value</c> on the
/// branch. That is why the choice is a mode the module names rather than something inferred from
/// the compiler in hand: it moves on an edit that can be reviewed, not on an SDK upgrade.
/// </para>
/// <para>
/// The converter is needed in both cases - the union feature says nothing about System.Text.Json -
/// so the discriminator and shape work is what carries over.
/// </para>
/// </remarks>
internal static class OneOfEmitter {

    /// <summary>The converter's type name, which the allocator reserves alongside the type.</summary>
    public static string ConverterName(string schemaName) =>
        NamingHelper.ToPascalCase(schemaName) + "Converter";

    public static ClassDefinition Emit(
        IConstructContainer container, SchemaModel schema, string modelsNamespace) {
        var name = NamingHelper.ToPascalCase(schema.Name);
        var branches = Branches(schema, modelsNamespace);
        var readable = Readable(branches);

        var type = container.AddClass(name);

        type.TypeKeyword = ClassKeyword.Struct;
        type.Modifiers |= ComponentModifier.Public;
        type.Comment = DocComment.Format(schema.Description) ?? $"One of {readable}.";

        EmitValue(type);
        EmitConstructors(type, name, branches);
        EmitConversions(type, name, branches);
        EmitToString(type);

        return type;
    }

    /// <summary>
    /// Nullable, because <c>default</c> bypasses the constructor. Nothing here produces one, and a
    /// caller who writes it gets a value that matches no branch in a <c>switch</c> rather than a
    /// null reference at some later point.
    /// </summary>
    private static void EmitValue(ClassDefinition type) {
        var value = type.AddProperty(TypeDefinition.Get(typeof(object)).MakeNullable(), "Value");

        value.Modifiers |= ComponentModifier.Public;
        value.Comment = "The value, which is one of the types this may hold.";
        value.Set = null;
    }

    /// <summary>
    /// One constructor per branch, so the branch set is stated to the compiler rather than to a
    /// run-time check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was a single constructor over <c>object</c> that threw <c>ArgumentException</c> for
    /// anything that was not a branch. One per branch says the same thing earlier: a value the
    /// schema does not permit is now a build error at the call site rather than a throw on the line
    /// that constructed it, and the message the old check composed had nowhere left to be needed.
    /// </para>
    /// <para>
    /// It is also the shape the language recognises. A type with one single-parameter constructor
    /// per case and a public <c>object? Value</c> is what <c>union OneOfHolderPayload(Cat, Dog);</c>
    /// compiles to, so this differs from the native declaration in nothing a caller can observe -
    /// which is what lets the two be swapped without touching code that uses either.
    /// </para>
    /// <para>
    /// The one case this changes rather than moves: <see cref="Branches"/> drops a branch it could
    /// not type, so a value of that branch had no constructor to reach anyway. It used to reach the
    /// <c>object</c> one and throw; now there is nothing to call. A refusal at build time rather
    /// than at run time, which is the direction worth moving in, but it is visible.
    /// </para>
    /// <para>
    /// Emitted as components rather than through <c>AddConstructor</c>, for the same reason the
    /// conversions below are: the branch is a qualified name rather than a type this assembly can
    /// reference, and taking both from one list is what keeps a constructor and its conversion from
    /// ever disagreeing about the branch set.
    /// </para>
    /// </remarks>
    private static void EmitConstructors(ClassDefinition type, string name, List<string> branches) {
        foreach (var branch in branches) {
            type.AddComponent(
                new CodeOutputComponent($"public {name}({branch} value) => Value = value;") {
                    Indented = true
                });
        }
    }

    /// <summary>
    /// One conversion per branch, so assigning a <c>Cat</c> where the payload goes is checked by the
    /// compiler rather than by the constructor at run time.
    /// </summary>
    private static void EmitConversions(ClassDefinition type, string name, List<string> branches) {
        foreach (var branch in branches) {
            type.AddComponent(
                new CodeOutputComponent(
                    $"public static implicit operator {name}({branch} value) => new(value);") {
                    Indented = true
                });
        }
    }

    private static void EmitToString(ClassDefinition type) {
        var method = type.AddMethod("ToString");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.SetReturnType(TypeDefinition.Get(typeof(string)));
        method.AddIndentedStatement("return Value?.ToString() ?? \"\"");
    }

    /// <summary>The branch types, qualified, in the order the document declares them.</summary>
    public static List<string> Branches(SchemaModel schema, string modelsNamespace) {
        var branches = new List<string>();

        foreach (var described in schema.OneOf) {
            var branch = TypeMapper.QualifiedName(
                modelsNamespace, ChoiceResolution.CSharpType(described), false);

            // A branch this parser could not type reads as JsonElement, which every other branch
            // would also accept - so it is not one of the types the wrapper distinguishes.
            if (branch.EndsWith("JsonElement", System.StringComparison.Ordinal)) {
                continue;
            }

            if (!branches.Contains(branch)) {
                branches.Add(branch);
            }
        }

        return branches;
    }

    /// <summary>The branch list as prose, for a message a person reads.</summary>
    private static string Readable(List<string> branches) {
        var names = new List<string>();

        foreach (var branch in branches) {
            var lastDot = branch.LastIndexOf('.');

            names.Add(lastDot >= 0 ? branch.Substring(lastDot + 1) : branch);
        }

        return names.Count switch {
            0 => "nothing",
            1 => names[0],
            _ => string.Join(", ", names.GetRange(0, names.Count - 1)) + " or " + names[names.Count - 1]
        };
    }
}

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Hardened.OpenApiDocument.BuildTask.Tests;

/// <summary>
/// Writes an assembly carrying a served document in the shape the generators emit, in either
/// lowering the C# compiler has used, without a compiler.
/// </summary>
/// <remarks>
/// <para>
/// The compiler in the pinned SDK lowers a constant <c>ReadOnlySpan&lt;byte&gt;</c> one way, and
/// the assemblies the integration applications build cover that way. The other lowering -
/// <c>ldtoken</c> of the data field and a call to <c>RuntimeHelpers.CreateSpan</c> - is what a
/// compiler emits for wider element types today and may emit for bytes tomorrow, and no compiler on
/// this machine will produce it for a byte span. So this writes the metadata and the IL directly,
/// which is also what makes the failure cases - a getter with no data field, two entry points -
/// reachable at all.
/// </para>
/// <para>
/// The shape is what <c>OpenApiDocumentSource</c> emits: a public class in a namespace, a nested
/// static class named <c>OpenApiDocument</c>, and a static getter <c>get_GZip</c> returning
/// <c>ReadOnlySpan&lt;byte&gt;</c> over a field of <c>&lt;PrivateImplementationDetails&gt;</c> with a
/// relative virtual address.
/// </para>
/// </remarks>
public static class PeFixture {

    public enum Lowering {
        /// <summary><c>ldsflda</c>, <c>ldc.i4</c>, <c>newobj</c>.</summary>
        FieldAddress,

        /// <summary><c>ldtoken</c>, <c>call CreateSpan&lt;byte&gt;</c>.</summary>
        FieldToken,

        /// <summary>A getter that returns a default span and references no field at all.</summary>
        NoField
    }

    /// <summary>One served document to write: the entry point's name and the bytes.</summary>
    public sealed record Document(string EntryPoint, byte[] Compressed, Lowering Lowering, int? DeclaredLength = null);

    /// <summary>Writes an assembly with the given documents to <paramref name="path"/>.</summary>
    public static void Write(string path, params Document[] documents) {
        File.WriteAllBytes(path, Build(documents));
    }

    public static byte[] Build(params Document[] documents) {
        var metadata = new MetadataBuilder();
        var il = new BlobBuilder();
        var mappedFieldData = new BlobBuilder();

        metadata.AddModule(
            0, metadata.GetOrAddString("Fixture.dll"), metadata.GetOrAddGuid(Guid.NewGuid()),
            default, default);

        metadata.AddAssembly(
            metadata.GetOrAddString("Fixture"), new Version(1, 0, 0, 0), default, default,
            0, AssemblyHashAlgorithm.None);

        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"), new Version(8, 0, 0, 0), default,
            default, 0, default);

        var objectType = TypeRef(metadata, systemRuntime, "System", "Object");
        var valueType = TypeRef(metadata, systemRuntime, "System", "ValueType");
        var readOnlySpanType = TypeRef(metadata, systemRuntime, "System", "ReadOnlySpan`1");
        var runtimeHelpersType = TypeRef(metadata, systemRuntime, "System.Runtime.CompilerServices", "RuntimeHelpers");
        var runtimeFieldHandleType = TypeRef(metadata, systemRuntime, "System", "RuntimeFieldHandle");

        // ReadOnlySpan<byte>, as a type specification, for the getter's return and the constructor.
        var spanOfByteSignature = new BlobBuilder();

        new BlobEncoder(spanOfByteSignature).TypeSpecificationSignature()
            .GenericInstantiation(readOnlySpanType, 1, isValueType: true)
            .AddArgument().Byte();

        var spanOfByte = metadata.AddTypeSpecification(metadata.GetOrAddBlob(spanOfByteSignature));

        // ReadOnlySpan<byte>..ctor(void*, int)
        var constructorSignature = new BlobBuilder();

        new BlobEncoder(constructorSignature).MethodSignature(isInstanceMethod: true)
            .Parameters(2,
                returnType => returnType.Void(),
                parameters => {
                    parameters.AddParameter().Type().VoidPointer();
                    parameters.AddParameter().Type().Int32();
                });

        var spanConstructor = metadata.AddMemberReference(
            spanOfByte, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(constructorSignature));

        // RuntimeHelpers.CreateSpan<T>(RuntimeFieldHandle), instantiated at byte.
        var createSpanSignature = new BlobBuilder();

        new BlobEncoder(createSpanSignature).MethodSignature(genericParameterCount: 1)
            .Parameters(1,
                returnType => returnType.Type()
                    .GenericInstantiation(readOnlySpanType, 1, isValueType: true)
                    .AddArgument().GenericMethodTypeParameter(0),
                parameters => parameters.AddParameter().Type().Type(runtimeFieldHandleType, isValueType: true));

        var createSpan = metadata.AddMemberReference(
            runtimeHelpersType, metadata.GetOrAddString("CreateSpan"), metadata.GetOrAddBlob(createSpanSignature));

        var createSpanOfByteSignature = new BlobBuilder();

        new BlobEncoder(createSpanOfByteSignature).MethodSpecificationSignature(1).AddArgument().Byte();

        var createSpanOfByte = metadata.AddMethodSpecification(
            createSpan, metadata.GetOrAddBlob(createSpanOfByteSignature));

        // <Module>, which has to be the first type.
        metadata.AddTypeDefinition(
            default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        // <PrivateImplementationDetails>, with one sized struct and one field per document.
        var details = metadata.AddTypeDefinition(
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default, metadata.GetOrAddString("<PrivateImplementationDetails>"), objectType,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var fields = new List<FieldDefinitionHandle>();

        foreach (var document in documents) {
            if (document.Lowering == Lowering.NoField) {
                fields.Add(default);

                continue;
            }

            var size = document.Compressed.Length;

            var arrayType = metadata.AddTypeDefinition(
                TypeAttributes.NestedAssembly | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
                default, metadata.GetOrAddString("__StaticArrayInitTypeSize=" + size), valueType,
                MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1),
                MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1));

            metadata.AddNestedType(arrayType, details);
            metadata.AddTypeLayout(arrayType, packingSize: 1, size: (uint)size);

            var fieldSignature = new BlobBuilder();

            new BlobEncoder(fieldSignature).FieldSignature().Type(arrayType, isValueType: true);

            var field = metadata.AddFieldDefinition(
                FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA,
                metadata.GetOrAddString("Data" + fields.Count),
                metadata.GetOrAddBlob(fieldSignature));

            mappedFieldData.Align(8);
            metadata.AddFieldRelativeVirtualAddress(field, mappedFieldData.Count);
            mappedFieldData.WriteBytes(document.Compressed);

            fields.Add(field);
        }

        // The entry points and their nested OpenApiDocument types.
        var getterSignature = new BlobBuilder();

        new BlobEncoder(getterSignature).MethodSignature()
            .Parameters(0,
                returnType => returnType.Type()
                    .GenericInstantiation(readOnlySpanType, 1, isValueType: true)
                    .AddArgument().Byte(),
                _ => { });

        var getterSignatureBlob = metadata.GetOrAddBlob(getterSignature);
        var bodies = new MethodBodyStreamEncoder(il);

        for (var index = 0; index < documents.Length; index++) {
            var document = documents[index];

            var entryPoint = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
                metadata.GetOrAddString("Fixture"), metadata.GetOrAddString(document.EntryPoint), objectType,
                MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1),
                MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1));

            var instructions = new InstructionEncoder(new BlobBuilder());

            switch (document.Lowering) {
                case Lowering.FieldAddress:
                    instructions.OpCode(ILOpCode.Ldsflda);
                    instructions.Token(fields[index]);
                    instructions.LoadConstantI4(document.DeclaredLength ?? document.Compressed.Length);
                    instructions.OpCode(ILOpCode.Newobj);
                    instructions.Token(spanConstructor);
                    break;
                case Lowering.FieldToken:
                    instructions.OpCode(ILOpCode.Ldtoken);
                    instructions.Token(fields[index]);
                    instructions.OpCode(ILOpCode.Call);
                    instructions.Token(createSpanOfByte);
                    break;
                default:
                    // ldnull; ldc.i4.0; newobj - a span over nothing, referencing no field.
                    instructions.OpCode(ILOpCode.Ldc_i4_0);
                    instructions.OpCode(ILOpCode.Conv_u);
                    instructions.OpCode(ILOpCode.Ldc_i4_0);
                    instructions.OpCode(ILOpCode.Newobj);
                    instructions.Token(spanConstructor);
                    break;
            }

            instructions.OpCode(ILOpCode.Ret);

            var bodyOffset = bodies.AddMethodBody(instructions, maxStack: 8);

            var nested = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class,
                default, metadata.GetOrAddString(ServedDocumentReader.DocumentTypeName), objectType,
                MetadataTokens.FieldDefinitionHandle(metadata.GetRowCount(TableIndex.Field) + 1),
                MetadataTokens.MethodDefinitionHandle(metadata.GetRowCount(TableIndex.MethodDef) + 1));

            metadata.AddNestedType(nested, entryPoint);

            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(ServedDocumentReader.GetterName),
                getterSignatureBlob,
                bodyOffset,
                MetadataTokens.ParameterHandle(metadata.GetRowCount(TableIndex.Param) + 1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            il,
            mappedFieldData: mappedFieldData);

        var blob = new BlobBuilder();

        pe.Serialize(blob);

        return blob.ToArray();
    }

    private static TypeReferenceHandle TypeRef(
        MetadataBuilder metadata, AssemblyReferenceHandle scope, string ns, string name) =>
        metadata.AddTypeReference(scope, metadata.GetOrAddString(ns), metadata.GetOrAddString(name));
}

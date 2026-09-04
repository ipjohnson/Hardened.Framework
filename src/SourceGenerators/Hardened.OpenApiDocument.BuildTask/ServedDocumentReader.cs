using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Hardened.OpenApiDocument.BuildTask;

/// <summary>
/// Reads the served OpenAPI document out of a compiled assembly's metadata.
/// </summary>
/// <remarks>
/// <para>
/// The document reaches the assembly as a gzipped <c>ReadOnlySpan&lt;byte&gt;</c> literal, which the
/// C# compiler stores as data in the PE: a field of <c>&lt;PrivateImplementationDetails&gt;</c> with
/// a relative virtual address, referenced from the generated getter. This finds that getter by
/// name, decodes its one-statement body for the field token it references, resolves the field's
/// address and size, and reads the bytes. Nothing is loaded and nothing is executed, so the
/// assembly can be a Lambda function, a library with no entry point, or a build for a runtime this
/// machine cannot run.
/// </para>
/// <para>
/// <b>The names.</b> <c>OpenApiDocumentSource</c> in the generators emits the literal under a static
/// class named <see cref="DocumentTypeName"/> nested in the entry point, with one static getter
/// named <see cref="GetterName"/>. Both front ends go through it, so a code-first and a normalised
/// spec-first document have the same shape here. Change a name there and change it here.
/// </para>
/// <para>
/// <b>The lowerings.</b> Roslyn has emitted a constant span two ways: <c>ldsflda</c> of the data
/// field, <c>ldc.i4</c> of its length and <c>newobj</c> of the span, and <c>ldtoken</c> of the field
/// followed by a call to <c>RuntimeHelpers.CreateSpan</c>. Both carry the field token, which is all
/// this needs; the length, where a lowering carries one, is checked against the field's own size so
/// a compiler that changed the shape fails loudly rather than exporting a truncated document. A
/// span of only a few elements is lowered element by element with no data field at all, which is
/// why fixtures for this reader are built from a real document rather than a stub.
/// </para>
/// </remarks>
internal static class ServedDocumentReader {

    public const string DocumentTypeName = "OpenApiDocument";

    public const string PropertyName = "GZip";

    public const string GetterName = "get_" + PropertyName;

    /// <summary>What the served document begins with, uncompressed.</summary>
    public const string ExpectedPrefix = "{\"openapi\"";

    /// <summary>
    /// One served document: the entry point it belongs to and its bytes as the assembly holds them.
    /// </summary>
    internal sealed class ServedDocument {
        public ServedDocument(string entryPoint, byte[] compressed, string lowering) {
            EntryPoint = entryPoint;
            Compressed = compressed;
            Lowering = lowering;
        }

        /// <summary>The full name of the entry point whose nested type carried the literal.</summary>
        public string EntryPoint { get; }

        public byte[] Compressed { get; }

        /// <summary>The shape the getter was found in, for a test to say which one it covered.</summary>
        public string Lowering { get; }
    }

    /// <summary>
    /// Every served document in the assembly at <paramref name="assemblyPath"/>.
    /// </summary>
    /// <exception cref="ServedDocumentException">
    /// A getter was found whose body is not in a shape this reader knows.
    /// </exception>
    public static IReadOnlyList<ServedDocument> Read(string assemblyPath) {
        using var stream = OpenShared(assemblyPath);
        using var reader = new PEReader(stream);

        var metadata = reader.GetMetadataReader();
        var documents = new List<ServedDocument>();

        foreach (var handle in metadata.TypeDefinitions) {
            var type = metadata.GetTypeDefinition(handle);

            if (!type.IsNested || metadata.GetString(type.Name) != DocumentTypeName) {
                continue;
            }

            var entryPoint = FullName(metadata, metadata.GetTypeDefinition(type.GetDeclaringType()));

            foreach (var methodHandle in type.GetMethods()) {
                var method = metadata.GetMethodDefinition(methodHandle);

                if (metadata.GetString(method.Name) != GetterName) {
                    continue;
                }

                documents.Add(ReadLiteral(reader, metadata, method, entryPoint));
            }
        }

        return documents;
    }

    /// <summary>Inflates what <see cref="Read"/> returned.</summary>
    public static byte[] Inflate(byte[] compressed) {
        using var source = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return inflated.ToArray();
    }

    /// <summary>
    /// Shared read access, retried. Windows can hold a freshly written assembly for a moment after
    /// the compiler closes it, which the SDK's own Copy task answers with a short retry; a build
    /// task that failed on that would fail one build in fifty and never the next.
    /// </summary>
    private static FileStream OpenShared(string path) {
        const int attempts = 10;

        for (var attempt = 1; ; attempt++) {
            try {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException) when (attempt < attempts) {
                Thread.Sleep(100);
            }
        }
    }

    private static ServedDocument ReadLiteral(
        PEReader reader, MetadataReader metadata, MethodDefinition getter, string entryPoint) {
        if (getter.RelativeVirtualAddress == 0) {
            throw new ServedDocumentException(entryPoint, "the getter has no body");
        }

        var body = reader.GetMethodBody(getter.RelativeVirtualAddress);
        var scan = IlScanner.Scan(body.GetILBytes() ?? Array.Empty<byte>(), metadata);

        if (scan.Field == null) {
            throw new ServedDocumentException(entryPoint, "the getter's body references no data field");
        }

        var field = metadata.GetFieldDefinition(scan.Field.Value);
        var rva = field.GetRelativeVirtualAddress();

        if (rva == 0) {
            throw new ServedDocumentException(
                entryPoint, "the field the getter references carries no data of its own");
        }

        var size = FieldSize(metadata, field);

        if (scan.DeclaredLength.HasValue && scan.DeclaredLength.Value != size) {
            throw new ServedDocumentException(
                entryPoint,
                $"the getter declares a length of {scan.DeclaredLength.Value} bytes and the data field is {size} bytes");
        }

        var block = reader.GetSectionData(rva);

        if (block.Length < size) {
            throw new ServedDocumentException(
                entryPoint, $"the data field claims {size} bytes and its section holds {block.Length}");
        }

        return new ServedDocument(entryPoint, block.GetContent(0, size).ToArray(), scan.Lowering.ToString());
    }

    /// <summary>
    /// The size of a field from its type: the explicit layout of the compiler's
    /// <c>__StaticArrayInitTypeSize=N</c> struct, or the width of a primitive where the compiler
    /// used one.
    /// </summary>
    private static int FieldSize(MetadataReader metadata, FieldDefinition field) {
        var signature = metadata.GetBlobReader(field.Signature);

        signature.ReadSignatureHeader();

        var typeCode = signature.ReadSignatureTypeCode();

        switch (typeCode) {
            case SignatureTypeCode.SByte:
            case SignatureTypeCode.Byte:
            case SignatureTypeCode.Boolean:
                return 1;
            case SignatureTypeCode.Int16:
            case SignatureTypeCode.UInt16:
            case SignatureTypeCode.Char:
                return 2;
            case SignatureTypeCode.Int32:
            case SignatureTypeCode.UInt32:
            case SignatureTypeCode.Single:
                return 4;
            case SignatureTypeCode.Int64:
            case SignatureTypeCode.UInt64:
            case SignatureTypeCode.Double:
                return 8;
            case SignatureTypeCode.TypeHandle:
                var typeHandle = signature.ReadTypeHandle();

                if (typeHandle.Kind == HandleKind.TypeDefinition) {
                    var layout = metadata.GetTypeDefinition((TypeDefinitionHandle)typeHandle).GetLayout();

                    if (layout.Size > 0) {
                        return layout.Size;
                    }
                }

                throw new InvalidOperationException("The data field's type declares no size.");
            default:
                throw new InvalidOperationException($"The data field has a type this reader does not size: {typeCode}.");
        }
    }

    private static string FullName(MetadataReader metadata, TypeDefinition type) {
        var name = metadata.GetString(type.Name);

        if (type.IsNested) {
            return FullName(metadata, metadata.GetTypeDefinition(type.GetDeclaringType())) + "." + name;
        }

        var ns = metadata.GetString(type.Namespace);

        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>
    /// Walks a method body once, keeping the first field token it references and the last
    /// <c>ldc.i4</c> constant it saw before that field's consumer.
    /// </summary>
    /// <remarks>
    /// A real decoder rather than a byte search. Operands are read by size so that a byte inside a
    /// token or a constant is never mistaken for an opcode; the table is ECMA-335's, which is small
    /// and does not change.
    /// </remarks>
    private static class IlScanner {

        public readonly struct Result {
            public Result(FieldDefinitionHandle? field, int? declaredLength, Lowering lowering) {
                Field = field;
                DeclaredLength = declaredLength;
                Lowering = lowering;
            }

            public FieldDefinitionHandle? Field { get; }

            public int? DeclaredLength { get; }

            public Lowering Lowering { get; }
        }

        /// <summary>Which of the two shapes the getter's body was found in.</summary>
        public enum Lowering {
            None,

            /// <summary><c>ldsflda</c> of the field, <c>ldc.i4</c> of its length, <c>newobj</c> of the span.</summary>
            FieldAddress,

            /// <summary><c>ldtoken</c> of the field, then <c>RuntimeHelpers.CreateSpan</c>.</summary>
            FieldToken
        }

        public static Result Scan(byte[] il, MetadataReader metadata) {
            FieldDefinitionHandle? field = null;
            int? length = null;
            var lowering = Lowering.None;
            var offset = 0;

            while (offset < il.Length) {
                int opcode = il[offset++];

                if (opcode == 0xFE) {
                    if (offset >= il.Length) {
                        break;
                    }

                    opcode = 0x100 | il[offset++];
                }

                var operandSize = OperandSize(opcode, il, offset);

                switch (opcode) {
                    case 0x16: case 0x17: case 0x18: case 0x19: case 0x1A:
                    case 0x1B: case 0x1C: case 0x1D: case 0x1E:
                        // ldc.i4.0 through ldc.i4.8
                        length = opcode - 0x16;
                        break;
                    case 0x1F:
                        // ldc.i4.s
                        length = (sbyte)il[offset];
                        break;
                    case 0x20:
                        // ldc.i4
                        length = ReadInt32(il, offset);
                        break;
                    case 0x7F:
                    case 0xD0:
                        // ldsflda and ldtoken, the two ways Roslyn has referenced the data field.
                        if (field == null) {
                            var token = ReadInt32(il, offset);
                            var handle = MetadataTokens.EntityHandle(token);

                            if (handle.Kind == HandleKind.FieldDefinition) {
                                field = (FieldDefinitionHandle)handle;
                                lowering = opcode == 0x7F ? Lowering.FieldAddress : Lowering.FieldToken;
                            }
                        }

                        break;
                }

                offset += operandSize;
            }

            // The length is only meaningful in the ldsflda/ldc.i4/newobj shape. In the CreateSpan
            // shape no constant is loaded, and a stray ldc.i4 before ldtoken would be a compiler
            // this reader does not know.
            return new Result(field, lowering == Lowering.FieldAddress ? length : null, lowering);
        }

        private static int ReadInt32(byte[] il, int offset) =>
            il[offset] | (il[offset + 1] << 8) | (il[offset + 2] << 16) | (il[offset + 3] << 24);

        /// <summary>The operand width of an opcode, per ECMA-335 partition III.</summary>
        private static int OperandSize(int opcode, byte[] il, int offset) {
            if (opcode >= 0x100) {
                switch (opcode & 0xFF) {
                    case 0x06: // ldftn
                    case 0x07: // ldvirtftn
                    case 0x15: // initobj
                    case 0x16: // constrained.
                    case 0x1C: // sizeof
                        return 4;
                    case 0x09: // ldarg
                    case 0x0A: // ldarga
                    case 0x0B: // starg
                    case 0x0C: // ldloc
                    case 0x0D: // ldloca
                    case 0x0E: // stloc
                        return 2;
                    case 0x12: // unaligned.
                        return 1;
                    default:
                        return 0;
                }
            }

            switch (opcode) {
                case 0x0E: case 0x0F: case 0x10: case 0x11: case 0x12: case 0x13: // ldarg.s .. stloc.s
                case 0x1F: // ldc.i4.s
                case 0x2B: case 0x2C: case 0x2D: case 0x2E: case 0x2F: case 0x30: // br.s .. bge.s
                case 0x31: case 0x32: case 0x33: case 0x34: case 0x35: case 0x36: case 0x37: // bgt.s .. blt.un.s
                case 0xDE: // leave.s
                    return 1;
                case 0x20: // ldc.i4
                case 0x22: // ldc.r4
                case 0x27: case 0x28: case 0x29: // jmp, call, calli
                case 0x38: case 0x39: case 0x3A: case 0x3B: case 0x3C: case 0x3D: // br .. bge
                case 0x3E: case 0x3F: case 0x40: case 0x41: case 0x42: case 0x43: case 0x44: // bgt .. blt.un
                case 0x6F: case 0x70: case 0x71: case 0x72: case 0x73: case 0x74: case 0x75: // callvirt .. isinst
                case 0x79: case 0x7B: case 0x7C: case 0x7D: case 0x7E: case 0x7F: case 0x80: case 0x81: // unbox .. stsfld
                case 0x8C: case 0x8D: case 0x8F: // box, newarr, ldelema
                case 0xA3: case 0xA4: case 0xA5: // ldelem, stelem, unbox.any
                case 0xC2: case 0xC6: // refanyval, mkrefany
                case 0xD0: // ldtoken
                case 0xDD: // leave
                    return 4;
                case 0x21: // ldc.i8
                case 0x23: // ldc.r8
                    return 8;
                case 0x45: // switch: a count followed by that many targets
                    return 4 + 4 * ReadInt32(il, offset);
                default:
                    return 0;
            }
        }
    }
}

/// <summary>
/// A getter was found under the documented name, and its body is not something this reader can
/// take a document out of.
/// </summary>
internal sealed class ServedDocumentException : Exception {
    public ServedDocumentException(string entryPoint, string detail)
        : base(detail) {
        EntryPoint = entryPoint;
    }

    public string EntryPoint { get; }
}

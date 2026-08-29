using Hardened.Idl.Emitters;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

public class JsonTypeInfoEmitterTests {
    [Fact]
    public void Emit_SimpleRecord_GeneratesResolver() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Required = new List<string> { "id", "name" },
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "tag", Type = "string", IsRequired = false }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("namespace Test.Api.Models\n{", result);
        Assert.Contains("class PetstoreJsonTypeInfoResolver : IJsonTypeInfoResolver", result);
        // static readonly, not readonly static: the conventional order, which V1 wrote backwards.
        Assert.Contains("public static readonly PetstoreJsonTypeInfoResolver Instance = new();", result);
        Assert.Contains("if (type == typeof(global::Test.Api.Models.Pet)) return CreatePetTypeInfo(options);", result);
        Assert.Contains("ObjectWithParameterizedConstructorCreator", result);
        Assert.Contains("(string)args[0]", result);
        Assert.Contains("(string)args[1]", result);
        Assert.Contains("(string?)args[2]", result);
    }

    [Fact]
    public void Emit_SimpleRecord_GeneratesPropertyInfos() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Required = new List<string> { "id", "name" },
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "tag", Type = "string", IsRequired = false }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<string>(options", result);
        Assert.Contains("DeclaringType = typeof(global::Test.Api.Models.Pet)", result);
        Assert.Contains("PropertyName = \"id\"", result);
        Assert.Contains("PropertyName = \"name\"", result);
        Assert.Contains("PropertyName = \"tag\"", result);
        Assert.Contains("((global::Test.Api.Models.Pet)obj).Id", result);
        Assert.Contains("((global::Test.Api.Models.Pet)obj).Name", result);
        Assert.Contains("((global::Test.Api.Models.Pet)obj).Tag", result);
        Assert.Contains("Setter = null", result);
    }

    [Fact]
    public void Emit_SimpleRecord_GeneratesConstructorParameters() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Required = new List<string> { "id", "name" },
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "tag", Type = "string", IsRequired = false }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("ConstructorParameterMetadataInitializer", result);
        Assert.Contains("Name = \"id\"", result);
        Assert.Contains("Name = \"name\"", result);
        Assert.Contains("Name = \"tag\"", result);
        // Required params: HasDefaultValue = false
        Assert.Contains("Position = 0", result);
        Assert.Contains("Position = 1", result);
        Assert.Contains("Position = 2", result);
    }

    [Fact]
    public void Emit_PrimitiveTypes_MapsCorrectly() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "AllTypes",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "text", Type = "string", IsRequired = true },
                    new() { Name = "count", Type = "integer", IsRequired = true },
                    new() { Name = "bigCount", Type = "integer", Format = "int64", IsRequired = true },
                    new() { Name = "ratio", Type = "number", Format = "float", IsRequired = true },
                    new() { Name = "amount", Type = "number", Format = "double", IsRequired = true },
                    new() { Name = "flag", Type = "boolean", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<string>(options", result);
        Assert.Contains("CreatePropertyInfo<int>(options", result);
        Assert.Contains("CreatePropertyInfo<long>(options", result);
        // float, not global::System.Single. V1's keyword table listed double but not float, so the
        // one type here whose keyword was missing came out under its reflection name.
        Assert.Contains("CreatePropertyInfo<float>(options", result);
        Assert.Contains("CreatePropertyInfo<double>(options", result);
        Assert.Contains("CreatePropertyInfo<bool>(options", result);
        Assert.Contains("(string)args[0]", result);
        Assert.Contains("(int)args[1]", result);
        Assert.Contains("(long)args[2]", result);
        Assert.Contains("(float)args[3]", result);
        Assert.Contains("(double)args[4]", result);
        Assert.Contains("(bool)args[5]", result);
    }

    [Fact]
    public void Emit_StringFormats_MapsCorrectly() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Formatted",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "createdAt", Type = "string", Format = "date-time", IsRequired = true },
                    new() { Name = "birthDate", Type = "string", Format = "date", IsRequired = true },
                    new() { Name = "avatar", Type = "string", Format = "byte", IsRequired = true },
                    new() { Name = "file", Type = "string", Format = "binary", IsRequired = true },
                    new() { Name = "uuid", Type = "string", Format = "uuid", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.DateTimeOffset>(options", result);
        Assert.Contains("CreatePropertyInfo<global::System.DateOnly>(options", result);
        Assert.Contains("CreatePropertyInfo<global::System.Byte[]>(options", result);
        Assert.Contains("CreatePropertyInfo<string>(options", result);
        Assert.Contains("(global::System.DateTimeOffset)args[0]", result);
        Assert.Contains("(global::System.DateOnly)args[1]", result);
        Assert.Contains("(global::System.Byte[])args[2]", result);
    }

    [Fact]
    public void Emit_ObjectRef_GeneratesCorrectTypeInfo() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Owner",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                }
            },
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "owner", Ref = "#/components/schemas/Owner", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::Test.Api.Models.Owner>(options", result);
        Assert.Contains("((global::Test.Api.Models.Pet)obj).Owner", result);
        Assert.Contains("(global::Test.Api.Models.Owner)args[1]", result);
        Assert.Contains("ParameterType = typeof(global::Test.Api.Models.Owner)", result);
    }

    [Fact]
    public void Emit_ArrayOfPrimitives_GeneratesList() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "TagList",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "tags",
                        IsArray = true,
                        ArrayItemsType = "string",
                        IsRequired = true,
                    }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.List<string>>(options", result);
        Assert.Contains("(global::System.Collections.Generic.List<string>)args[0]", result);
        Assert.Contains("ParameterType = typeof(global::System.Collections.Generic.List<string>)", result);
    }

    [Fact]
    public void Emit_ArrayOfObjects_GeneratesListOfRef() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                }
            },
            new() {
                Name = "PetList",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "items",
                        IsArray = true,
                        ArrayItemsRef = "#/components/schemas/Pet",
                        IsRequired = true,
                    }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.List<global::Test.Api.Models.Pet>>(options", result);
        Assert.Contains("(global::System.Collections.Generic.List<global::Test.Api.Models.Pet>)args[0]", result);
    }

    [Fact]
    public void Emit_Dictionary_GeneratesDictionaryType() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Metadata",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "labels",
                        IsDictionary = true,
                        DictionaryValueType = "string",
                        IsRequired = true,
                    }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.Dictionary<string,string>>(options", result);
        Assert.Contains("(global::System.Collections.Generic.Dictionary<string,string>)args[0]", result);
    }

    [Fact]
    public void Emit_DictionaryWithObjectRef_GeneratesDictionaryOfRef() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                }
            },
            new() {
                Name = "PetMap",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "pets",
                        IsDictionary = true,
                        DictionaryValueRef = "#/components/schemas/Pet",
                        IsRequired = true,
                    }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.Dictionary<string,global::Test.Api.Models.Pet>>(options", result);
    }

    [Fact]
    public void Emit_EnumSchema_GeneratesGetEnumTypeInfo() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "PetStatus",
                Kind = SchemaKind.Enum,
                EnumValues = new List<string> { "available", "pending", "sold" }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("if (type == typeof(global::Test.Api.Models.PetStatus)) return CreatePetStatusTypeInfo(options);", result);
        Assert.Contains("CreateValueInfo<global::Test.Api.Models.PetStatus>(options", result);
    }

    [Fact]
    public void Emit_EnumRef_TreatsAsValueType() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "PetStatus",
                Kind = SchemaKind.Enum,
                EnumValues = new List<string> { "available", "pending", "sold" }
            },
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() {
                        Name = "status",
                        Ref = "#/components/schemas/PetStatus",
                        IsRequired = false
                    },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Optional enum should be nullable (value type)
        Assert.Contains("CreatePropertyInfo<global::Test.Api.Models.PetStatus?>(options", result);
        Assert.Contains("(global::Test.Api.Models.PetStatus?)args[1]", result);
        Assert.Contains("ParameterType = typeof(global::Test.Api.Models.PetStatus?)", result);
    }

    [Fact]
    public void Emit_NullableValueTypes_AddsNullableSuffix() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Stats",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "requiredCount", Type = "integer", IsRequired = true },
                    new() { Name = "optionalCount", Type = "integer", IsRequired = false },
                    new() { Name = "optionalFlag", Type = "boolean", IsRequired = false },
                    new() { Name = "optionalDate", Type = "string", Format = "date-time", IsRequired = false },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Required int: non-nullable
        Assert.Contains("CreatePropertyInfo<int>(options", result);
        Assert.Contains("(int)args[0]", result);

        // Optional int: nullable
        Assert.Contains("CreatePropertyInfo<int?>(options", result);
        Assert.Contains("(int?)args[1]", result);

        // Optional bool: nullable
        Assert.Contains("CreatePropertyInfo<bool?>(options", result);
        Assert.Contains("(bool?)args[2]", result);

        // Optional DateTime: nullable
        Assert.Contains("CreatePropertyInfo<global::System.DateTimeOffset?>(options", result);
        Assert.Contains("(global::System.DateTimeOffset?)args[3]", result);
    }

    [Fact]
    public void Emit_NullableReferenceTypes_NoChangeToGenericType() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Item",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "description", Type = "string", IsRequired = false },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Both required and optional string properties use CreatePropertyInfo<string>
        // (reference types don't change generic parameter for nullability)
        var matches = result.Split("CreatePropertyInfo<string>").Length - 1;
        Assert.Equal(2, matches);

        // But the constructor cast uses nullable annotation
        Assert.Contains("(string)args[0]", result);
        Assert.Contains("(string?)args[1]", result);
    }

    [Fact]
    public void Emit_RequiredVsOptional_CorrectHasDefaultValue() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Mixed",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "required", Type = "string", IsRequired = true },
                    new() { Name = "optional", Type = "string", IsRequired = false },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Required comes first in sorted order
        Assert.Contains("Position = 0,", result);
        Assert.Contains("Position = 1,", result);
    }

    [Fact]
    public void Emit_EmptyRecord_UsesObjectCreator() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "EmptyModel",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel>()
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("ObjectCreator = static () => new global::Test.Api.Models.EmptyModel()", result);
        Assert.DoesNotContain("ObjectWithParameterizedConstructorCreator", result);
        Assert.DoesNotContain("ConstructorParameterMetadataInitializer", result);
    }

    [Fact]
    public void Emit_NestedObjectsWithArrays_GeneratesCorrectly() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Tag",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                }
            },
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() {
                        Name = "tags",
                        IsArray = true,
                        ArrayItemsRef = "#/components/schemas/Tag",
                        IsRequired = false
                    },
                }
            },
            new() {
                Name = "PetStore",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "pets",
                        IsArray = true,
                        ArrayItemsRef = "#/components/schemas/Pet",
                        IsRequired = true,
                    },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // All three types are in the resolver
        Assert.Contains("typeof(global::Test.Api.Models.Tag)", result);
        Assert.Contains("typeof(global::Test.Api.Models.Pet)", result);
        Assert.Contains("typeof(global::Test.Api.Models.PetStore)", result);

        // Nested list types
        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.List<global::Test.Api.Models.Tag>>(options", result);
        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.List<global::Test.Api.Models.Pet>>(options", result);
    }

    [Fact]
    public void Emit_MixedObjectsAndEnums_DispatchesCorrectly() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Color",
                Kind = SchemaKind.Enum,
                EnumValues = new List<string> { "red", "green", "blue" }
            },
            new() {
                Name = "Shape",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() {
                        Name = "color",
                        Ref = "#/components/schemas/Color",
                        IsRequired = true
                    },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Enum dispatch
        Assert.Contains("if (type == typeof(global::Test.Api.Models.Color)) return CreateColorTypeInfo(options);", result);
        Assert.Contains("CreateValueInfo<global::Test.Api.Models.Color>(options", result);

        // Object dispatch
        Assert.Contains("if (type == typeof(global::Test.Api.Models.Shape)) return CreateShapeTypeInfo(options);", result);

        // Required enum ref is a value type, no nullable
        Assert.Contains("CreatePropertyInfo<global::Test.Api.Models.Color>(options", result);
        Assert.Contains("(global::Test.Api.Models.Color)args[1]", result);
    }

    [Fact]
    public void Emit_PreservesOriginalJsonPropertyNames() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "user-profile",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "first_name", Type = "string", IsRequired = true },
                    new() { Name = "last-name", Type = "string", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // C# type and property names are PascalCase
        Assert.Contains("typeof(global::Test.Api.Models.UserProfile)", result);
        Assert.Contains("CreateUserProfileTypeInfo", result);
        Assert.Contains("((global::Test.Api.Models.UserProfile)obj).FirstName", result);
        Assert.Contains("((global::Test.Api.Models.UserProfile)obj).LastName", result);
        // JSON property names preserve original OpenAPI casing
        Assert.Contains("PropertyName = \"first_name\"", result);
        Assert.Contains("PropertyName = \"last-name\"", result);
    }

    [Fact]
    public void Emit_NoSchemas_GeneratesEmptyResolver() {
        var schemas = new List<SchemaModel>();

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("class PetstoreJsonTypeInfoResolver : IJsonTypeInfoResolver", result);
        Assert.Contains("return null;", result);
        Assert.DoesNotContain("CreateObjectInfo", result);
    }

    [Fact]
    public void Emit_OnlyPrimitiveAndArraySchemas_SkipsNonObjectNonEnum() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "StringArray",
                Kind = SchemaKind.Array,
                ArrayItemsType = "string",
            },
            new() {
                Name = "MyString",
                Kind = SchemaKind.Primitive,
                Type = "string",
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Neither array nor primitive schemas produce type dispatch
        Assert.DoesNotContain("typeof(global::Test.Api.Models.StringArray)", result);
        Assert.DoesNotContain("typeof(global::Test.Api.Models.MyString)", result);
    }

    [Fact]
    public void Emit_Header_IncludesRequiredUsings() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Foo",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // The file header is written once by the task, not by each emitter.
        // #nullable enable is part of the file header the task writes once, not of any one emitter.
        Assert.Contains("using System;", result);
        Assert.Contains("using System.Collections.Generic;", result);
        Assert.Contains("using System.Text.Json;", result);
        Assert.Contains("using System.Text.Json.Serialization.Metadata;", result);
    }

    [Fact]
    public void Emit_OptionalObjectRef_NullableReferenceType() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Address",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "city", Type = "string", IsRequired = true },
                }
            },
            new() {
                Name = "Person",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() {
                        Name = "address",
                        Ref = "#/components/schemas/Address",
                        IsRequired = false
                    },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Optional object ref is a reference type — no change to generic param
        Assert.Contains("CreatePropertyInfo<global::Test.Api.Models.Address>(options", result);
        // But constructor cast uses nullable
        Assert.Contains("(global::Test.Api.Models.Address?)args[1]", result);
    }

    /// <summary>
    /// The BCL leaf types are answered by <c>PrimitiveJsonTypeInfoResolver</c> in the runtime, not by
    /// each generated resolver.
    /// </summary>
    /// <remarks>
    /// One copy per specification file, in a chain that already holds one resolver per specification
    /// file, is a table that cannot vary duplicated N times - and every copy pinned
    /// <c>JsonMetadataServices.StringConverter</c> and friends, which discards a converter the
    /// application registered. Emitting them again would not be wrong so much as unreachable: the
    /// runtime resolver is last in the chain, so a duplicate here only shadows it.
    /// </remarks>
    [Fact]
    public void Emit_OmitsPrimitiveTypeEntries() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Minimal",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.DoesNotContain("if (type == typeof(string))", result);
        Assert.DoesNotContain("if (type == typeof(bool))", result);
        Assert.DoesNotContain("if (type == typeof(int))", result);
        Assert.DoesNotContain("if (type == typeof(long))", result);
        Assert.DoesNotContain("if (type == typeof(float))", result);
        Assert.DoesNotContain("if (type == typeof(double))", result);
        Assert.DoesNotContain("if (type == typeof(byte[]))", result);
        // Qualified, because the resolver also carries a StringConverters array - the generated enum
        // converters, as the parameter binder consumes them. What this is about is the primitive
        // type-info entries, which are JsonMetadataServices' own.
        Assert.DoesNotContain("JsonMetadataServices.StringConverter", result);
        Assert.DoesNotContain("BooleanConverter", result);
        Assert.DoesNotContain("Int32Converter", result);
        Assert.DoesNotContain("ByteArrayConverter", result);

        Assert.DoesNotContain("if (type == typeof(bool?))", result);
        Assert.DoesNotContain("if (type == typeof(int?))", result);
        Assert.DoesNotContain("GetNullableConverter<bool>", result);
        Assert.DoesNotContain("GetNullableConverter<int>", result);

        // What the resolver is still for: its own schema types, and the fallthrough.
        Assert.Contains("if (type == typeof(global::Test.Api.Models.Minimal)) return CreateMinimalTypeInfo(options);", result);
        Assert.Contains("return null;", result);

        // using directive
        Assert.Contains("using System.Text.Json.Serialization;", result);
    }

    [Fact]
    public void Emit_IncludesCollectionTypeEntries() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Pet",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                }
            },
            new() {
                Name = "PetStore",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "pets",
                        IsArray = true,
                        ArrayItemsRef = "#/components/schemas/Pet",
                        IsRequired = true,
                    },
                    new() {
                        Name = "tags",
                        IsArray = true,
                        ArrayItemsType = "string",
                        IsRequired = true,
                    },
                    new() {
                        Name = "metadata",
                        IsDictionary = true,
                        DictionaryValueType = "string",
                        IsRequired = true,
                    },
                    new() {
                        Name = "petMap",
                        IsDictionary = true,
                        DictionaryValueRef = "#/components/schemas/Pet",
                        IsRequired = true,
                    },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // List dispatch entries
        Assert.Contains("if (type == typeof(global::System.Collections.Generic.List<global::Test.Api.Models.Pet>)) return JsonMetadataServices.CreateListInfo<global::System.Collections.Generic.List<global::Test.Api.Models.Pet>, global::Test.Api.Models.Pet>(", result);
        Assert.Contains("if (type == typeof(global::System.Collections.Generic.List<string>)) return JsonMetadataServices.CreateListInfo<global::System.Collections.Generic.List<string>, string>(", result);
        Assert.Contains("ObjectCreator = static () => new global::System.Collections.Generic.List<global::Test.Api.Models.Pet>()", result);
        Assert.Contains("ObjectCreator = static () => new global::System.Collections.Generic.List<string>()", result);

        // Dictionary dispatch entries
        Assert.Contains("if (type == typeof(global::System.Collections.Generic.Dictionary<string,string>)) return JsonMetadataServices.CreateDictionaryInfo<global::System.Collections.Generic.Dictionary<string,string>, string, string>(", result);
        Assert.Contains("ObjectCreator = static () => new global::System.Collections.Generic.Dictionary<string,string>()", result);
        Assert.Contains("if (type == typeof(global::System.Collections.Generic.Dictionary<string,global::Test.Api.Models.Pet>)) return JsonMetadataServices.CreateDictionaryInfo<global::System.Collections.Generic.Dictionary<string,global::Test.Api.Models.Pet>, string, global::Test.Api.Models.Pet>(", result);
        Assert.Contains("ObjectCreator = static () => new global::System.Collections.Generic.Dictionary<string,global::Test.Api.Models.Pet>()", result);
    }

    [Fact]
    public void Emit_UntypedProperty_MapsToJsonElement() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Flexible",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "details", IsRequired = false },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Untyped property maps to JsonElement (value type), optional => nullable
        Assert.Contains("CreatePropertyInfo<global::System.Text.Json.JsonElement?>(options", result);
        Assert.Contains("(global::System.Text.Json.JsonElement?)args[1]", result);
        Assert.Contains("ParameterType = typeof(global::System.Text.Json.JsonElement?)", result);
    }

    [Fact]
    public void Emit_UntypedDictionary_MapsToJsonElementValues() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Metadata",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "data",
                        IsDictionary = true,
                        IsRequired = true,
                    }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.Dictionary<string,global::System.Text.Json.JsonElement>>(options", result);
        Assert.Contains("(global::System.Collections.Generic.Dictionary<string,global::System.Text.Json.JsonElement>)args[0]", result);
        Assert.Contains("if (type == typeof(global::System.Collections.Generic.Dictionary<string,global::System.Text.Json.JsonElement>))", result);
    }

    [Fact]
    public void Emit_UntypedArray_MapsToJsonElementList() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Container",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() {
                        Name = "items",
                        IsArray = true,
                        IsRequired = true,
                    }
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.Collections.Generic.List<global::System.Text.Json.JsonElement>>(options", result);
        Assert.Contains("(global::System.Collections.Generic.List<global::System.Text.Json.JsonElement>)args[0]", result);
        Assert.Contains("if (type == typeof(global::System.Collections.Generic.List<global::System.Text.Json.JsonElement>))", result);
    }

    /// <summary>
    /// <c>JsonElement</c> is what an unmapped schema falls to, so it is the leaf type most likely to
    /// appear without anyone asking for it - and it moved to the runtime resolver with the rest.
    /// </summary>
    [Fact]
    public void Emit_OmitsJsonElementPrimitiveEntries() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Minimal",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.DoesNotContain("if (type == typeof(global::System.Text.Json.JsonElement))", result);
        Assert.DoesNotContain("if (type == typeof(global::System.Text.Json.JsonElement?))", result);
        Assert.DoesNotContain("JsonElementConverter", result);
    }

    [Fact]
    public void Emit_RequiredValueTypes_DefaultValueIsTypeDefault() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "SyncRequest",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "lastSyncTimestamp", Type = "integer", Format = "int64", IsRequired = true },
                    new() {
                        Name = "entityTypes",
                        IsArray = true,
                        ArrayItemsType = "string",
                        IsRequired = true,
                    },
                    new() {
                        Name = "etags",
                        IsDictionary = true,
                        DictionaryValueType = "string",
                        IsRequired = false,
                    },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        // Required value type (long) must use default(long), not null
        Assert.Contains("DefaultValue = default(long)", result);

        // Required reference type (List<string>) should use null
        // Optional reference type (Dictionary<string, string>) should use null
        var nullDefaultCount = result.Split("DefaultValue = null").Length - 1;
        Assert.Equal(2, nullDefaultCount);
    }

    [Fact]
    public void Emit_OptionalDateOnly_NullableValueType() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Event",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "name", Type = "string", IsRequired = true },
                    new() { Name = "eventDate", Type = "string", Format = "date", IsRequired = false },
                }
            }
        };

        var result = EmitterHarness.JsonTypeInfo(schemas, "petstore");

        Assert.Contains("CreatePropertyInfo<global::System.DateOnly?>(options", result);
        Assert.Contains("(global::System.DateOnly?)args[1]", result);
        Assert.Contains("ParameterType = typeof(global::System.DateOnly?)", result);
    }
}

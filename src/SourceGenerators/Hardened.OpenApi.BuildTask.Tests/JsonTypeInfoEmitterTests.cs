using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("namespace Test.Api.Models;", result);
        Assert.Contains("class PetstoreJsonTypeInfoResolver : IJsonTypeInfoResolver", result);
        Assert.Contains("public static readonly PetstoreJsonTypeInfoResolver Instance = new();", result);
        Assert.Contains("if (type == typeof(Pet)) return CreatePetTypeInfo(options);", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<string>(options", result);
        Assert.Contains("DeclaringType = typeof(Pet)", result);
        Assert.Contains("PropertyName = \"id\"", result);
        Assert.Contains("PropertyName = \"name\"", result);
        Assert.Contains("PropertyName = \"tag\"", result);
        Assert.Contains("((Pet)obj).Id", result);
        Assert.Contains("((Pet)obj).Name", result);
        Assert.Contains("((Pet)obj).Tag", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<string>(options", result);
        Assert.Contains("CreatePropertyInfo<int>(options", result);
        Assert.Contains("CreatePropertyInfo<long>(options", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<DateTime>(options", result);
        Assert.Contains("CreatePropertyInfo<DateOnly>(options", result);
        Assert.Contains("CreatePropertyInfo<byte[]>(options", result);
        Assert.Contains("CreatePropertyInfo<string>(options", result);
        Assert.Contains("(DateTime)args[0]", result);
        Assert.Contains("(DateOnly)args[1]", result);
        Assert.Contains("(byte[])args[2]", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<Owner>(options", result);
        Assert.Contains("((Pet)obj).Owner", result);
        Assert.Contains("(Owner)args[1]", result);
        Assert.Contains("ParameterType = typeof(Owner)", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<List<string>>(options", result);
        Assert.Contains("(List<string>)args[0]", result);
        Assert.Contains("ParameterType = typeof(List<string>)", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<List<Pet>>(options", result);
        Assert.Contains("(List<Pet>)args[0]", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<Dictionary<string, string>>(options", result);
        Assert.Contains("(Dictionary<string, string>)args[0]", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<Dictionary<string, Pet>>(options", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("if (type == typeof(PetStatus)) return CreatePetStatusTypeInfo(options);", result);
        Assert.Contains("CreateValueInfo<PetStatus>(options", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Optional enum should be nullable (value type)
        Assert.Contains("CreatePropertyInfo<PetStatus?>(options", result);
        Assert.Contains("(PetStatus?)args[1]", result);
        Assert.Contains("ParameterType = typeof(PetStatus?)", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

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
        Assert.Contains("CreatePropertyInfo<DateTime?>(options", result);
        Assert.Contains("(DateTime?)args[3]", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Required comes first in sorted order
        Assert.Contains("Position = 0,\n                    HasDefaultValue = false,", result);
        Assert.Contains("Position = 1,\n                    HasDefaultValue = true,", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("ObjectCreator = static () => new EmptyModel()", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // All three types are in the resolver
        Assert.Contains("typeof(Tag)", result);
        Assert.Contains("typeof(Pet)", result);
        Assert.Contains("typeof(PetStore)", result);

        // Nested list types
        Assert.Contains("CreatePropertyInfo<List<Tag>>(options", result);
        Assert.Contains("CreatePropertyInfo<List<Pet>>(options", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Enum dispatch
        Assert.Contains("if (type == typeof(Color)) return CreateColorTypeInfo(options);", result);
        Assert.Contains("CreateValueInfo<Color>(options", result);

        // Object dispatch
        Assert.Contains("if (type == typeof(Shape)) return CreateShapeTypeInfo(options);", result);

        // Required enum ref is a value type, no nullable
        Assert.Contains("CreatePropertyInfo<Color>(options", result);
        Assert.Contains("(Color)args[1]", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // C# type and property names are PascalCase
        Assert.Contains("typeof(UserProfile)", result);
        Assert.Contains("CreateUserProfileTypeInfo", result);
        Assert.Contains("((UserProfile)obj).FirstName", result);
        Assert.Contains("((UserProfile)obj).LastName", result);
        // JSON property names preserve original OpenAPI casing
        Assert.Contains("PropertyName = \"first_name\"", result);
        Assert.Contains("PropertyName = \"last-name\"", result);
    }

    [Fact]
    public void Emit_NoSchemas_GeneratesEmptyResolver() {
        var schemas = new List<SchemaModel>();

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Neither array nor primitive schemas produce type dispatch
        Assert.DoesNotContain("typeof(StringArray)", result);
        Assert.DoesNotContain("typeof(MyString)", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("// <auto-generated/>", result);
        Assert.Contains("#nullable enable", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Optional object ref is a reference type — no change to generic param
        Assert.Contains("CreatePropertyInfo<Address>(options", result);
        // But constructor cast uses nullable
        Assert.Contains("(Address?)args[1]", result);
    }

    [Fact]
    public void Emit_IncludesPrimitiveTypeEntries() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Minimal",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                }
            }
        };

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Primitive types
        Assert.Contains("if (type == typeof(string)) return JsonMetadataServices.CreateValueInfo<string>(options, JsonMetadataServices.StringConverter);", result);
        Assert.Contains("if (type == typeof(bool))", result);
        Assert.Contains("if (type == typeof(int))", result);
        Assert.Contains("if (type == typeof(long))", result);
        Assert.Contains("if (type == typeof(float))", result);
        Assert.Contains("if (type == typeof(double))", result);
        Assert.Contains("if (type == typeof(DateTime))", result);
        Assert.Contains("if (type == typeof(DateOnly))", result);
        Assert.Contains("if (type == typeof(byte[]))", result);
        Assert.Contains("BooleanConverter", result);
        Assert.Contains("Int32Converter", result);
        Assert.Contains("Int64Converter", result);
        Assert.Contains("SingleConverter", result);
        Assert.Contains("DoubleConverter", result);
        Assert.Contains("DateTimeConverter", result);
        Assert.Contains("DateOnlyConverter", result);
        Assert.Contains("ByteArrayConverter", result);

        // Nullable value types
        Assert.Contains("if (type == typeof(bool?))", result);
        Assert.Contains("if (type == typeof(int?))", result);
        Assert.Contains("if (type == typeof(long?))", result);
        Assert.Contains("if (type == typeof(float?))", result);
        Assert.Contains("if (type == typeof(double?))", result);
        Assert.Contains("if (type == typeof(DateTime?))", result);
        Assert.Contains("if (type == typeof(DateOnly?))", result);
        Assert.Contains("GetNullableConverter<bool>", result);
        Assert.Contains("GetNullableConverter<int>", result);
        Assert.Contains("GetNullableConverter<long>", result);
        Assert.Contains("GetNullableConverter<float>", result);
        Assert.Contains("GetNullableConverter<double>", result);
        Assert.Contains("GetNullableConverter<DateTime>", result);
        Assert.Contains("GetNullableConverter<DateOnly>", result);

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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // List dispatch entries
        Assert.Contains("if (type == typeof(List<Pet>)) return JsonMetadataServices.CreateListInfo<List<Pet>, Pet>(", result);
        Assert.Contains("if (type == typeof(List<string>)) return JsonMetadataServices.CreateListInfo<List<string>, string>(", result);
        Assert.Contains("ObjectCreator = static () => new List<Pet>()", result);
        Assert.Contains("ObjectCreator = static () => new List<string>()", result);

        // Dictionary dispatch entries
        Assert.Contains("if (type == typeof(Dictionary<string, string>)) return JsonMetadataServices.CreateDictionaryInfo<Dictionary<string, string>, string, string>(", result);
        Assert.Contains("ObjectCreator = static () => new Dictionary<string, string>()", result);
        Assert.Contains("if (type == typeof(Dictionary<string, Pet>)) return JsonMetadataServices.CreateDictionaryInfo<Dictionary<string, Pet>, string, Pet>(", result);
        Assert.Contains("ObjectCreator = static () => new Dictionary<string, Pet>()", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        // Untyped property maps to JsonElement (value type), optional => nullable
        Assert.Contains("CreatePropertyInfo<JsonElement?>(options", result);
        Assert.Contains("(JsonElement?)args[1]", result);
        Assert.Contains("ParameterType = typeof(JsonElement?)", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<Dictionary<string, JsonElement>>(options", result);
        Assert.Contains("(Dictionary<string, JsonElement>)args[0]", result);
        Assert.Contains("if (type == typeof(Dictionary<string, JsonElement>))", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<List<JsonElement>>(options", result);
        Assert.Contains("(List<JsonElement>)args[0]", result);
        Assert.Contains("if (type == typeof(List<JsonElement>))", result);
    }

    [Fact]
    public void Emit_IncludesJsonElementPrimitiveEntries() {
        var schemas = new List<SchemaModel> {
            new() {
                Name = "Minimal",
                Kind = SchemaKind.Object,
                Properties = new List<PropertyModel> {
                    new() { Name = "id", Type = "string", IsRequired = true },
                }
            }
        };

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("if (type == typeof(JsonElement)) return JsonMetadataServices.CreateValueInfo<JsonElement>(options, JsonMetadataServices.JsonElementConverter);", result);
        Assert.Contains("if (type == typeof(JsonElement?)) return JsonMetadataServices.CreateValueInfo<JsonElement?>(options, JsonMetadataServices.GetNullableConverter<JsonElement>(options));", result);
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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

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

        var result = JsonTypeInfoEmitter.Emit(schemas, "Test.Api", "petstore");

        Assert.Contains("CreatePropertyInfo<DateOnly?>(options", result);
        Assert.Contains("(DateOnly?)args[1]", result);
        Assert.Contains("ParameterType = typeof(DateOnly?)", result);
    }
}

using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared;

public static class ClassDefinitionExtensions
{
    public static FieldDefinition AddUniqueField(this ClassDefinition classDefinition, ITypeDefinition typeDefinition, string prefix = "_unique")
    {
        var count = 1;
        var fieldName = prefix + count;

        while (classDefinition.Fields.Any(f => f.Name == fieldName))
        {
            count++;
        }

        return classDefinition.AddField(typeDefinition, fieldName);
    }
}
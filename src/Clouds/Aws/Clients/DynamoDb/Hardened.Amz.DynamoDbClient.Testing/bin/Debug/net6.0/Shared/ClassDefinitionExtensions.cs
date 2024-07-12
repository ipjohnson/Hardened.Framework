using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared;



public static class ClassDefinitionExtensions {
    public static FieldDefinition AddUniqueField(this ClassDefinition classDefinition, ITypeDefinition typeDefinition,
        string prefix = "_unique") {
        var count = 1;
        var fieldName = prefix + count;

        while (classDefinition.Fields.Any(f => f.Name == fieldName)) {
            count++;
        }

        return classDefinition.AddField(typeDefinition, fieldName);
    }

    public record ConstructorParameter(ITypeDefinition TypeDefinition, string Name);

    public static List<FieldDefinition> AddSimpleConstructor(
        this ClassDefinition classDefinition,
        params ConstructorParameter[] parameters) {
        var fields = new List<FieldDefinition>();
        var constructor = classDefinition.AddConstructor();
        
        foreach (var constructorParameter in parameters) {
            var field = classDefinition.AddField(constructorParameter.TypeDefinition,
                "_" + constructorParameter.Name);

            var parameter = constructor.AddParameter(constructorParameter.TypeDefinition,
                constructorParameter.Name);
            
            constructor.Assign(parameter).To(field.Name);
            fields.Add(field);
        }
        
        return fields;
    }
}
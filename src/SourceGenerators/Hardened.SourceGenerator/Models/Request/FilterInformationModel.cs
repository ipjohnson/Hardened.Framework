using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request
{
    public class FilterInformationModel
    {
        public FilterInformationModel(ITypeDefinition typeDefinition, string arguments, string propertyAssignment)
        {
            TypeDefinition = typeDefinition;
            Arguments = arguments;
            PropertyAssignment = propertyAssignment;
        }

        public ITypeDefinition TypeDefinition { get; }

        public string Arguments { get; }

        public string PropertyAssignment { get; }

        public override bool Equals(object obj)
        {
            if (obj is not FilterInformationModel filterInformationModel)
            {
                return false;
            }

            if (!TypeDefinition.Equals(filterInformationModel.TypeDefinition))
            {
                return false;
            }

            if (Arguments != filterInformationModel.Arguments)
            {
                return false;
            }

            if (PropertyAssignment != filterInformationModel.PropertyAssignment)
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return $"{TypeDefinition}:{Arguments}:{PropertyAssignment}";
        }
    }
}

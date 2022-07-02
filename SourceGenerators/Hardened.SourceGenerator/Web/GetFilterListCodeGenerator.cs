using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web
{
    public static class GetFilterListCodeGenerator
    {
        public static void Implement(WebEndPointModel endPointModel, ClassDefinition classDefinition)
        {
            var getFiltersMethod = classDefinition.AddMethod("GetFilterList");

            getFiltersMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

            getFiltersMethod.SetReturnType(KnownTypes.Requests.FilterFuncArray);

            getFiltersMethod.AddParameter(typeof(IServiceProvider), "serviceProvider");

            var filterArray = getFiltersMethod.Assign(NewArray(KnownTypes.Requests.FilterFunc, 0)).ToVar("filterArray");
            
            getFiltersMethod.Return(filterArray);
        }
    }
}

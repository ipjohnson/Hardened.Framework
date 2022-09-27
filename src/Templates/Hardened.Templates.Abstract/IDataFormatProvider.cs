namespace Hardened.Templates.Abstract
{
    public interface IDataFormatProvider
    {
        void ProvideFormatters(IDictionary<Type, FormatDataFunc> formatter);
    }
}

namespace Hardened.Templates.Abstract
{

    public interface IPropertyValue
    {
        string Name { get; }

        Type PropertyType { get; }

        object Value { get; }
    }

    public class PropertyValue
    {
        public static PropertyValue<T> From<T>(T value, string name)
        {
            return new PropertyValue<T>(value, name);
        }
    }

    public class PropertyValue<T> : IPropertyValue
    {
        public PropertyValue(T value, string name)
        {
            Value = value!;
            Name = name;
        }

        public object Value { get; }

        public string Name { get; }

        public Type PropertyType => typeof(T);

        public override string ToString()
        {
            return Value.ToString() ?? "";
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing.Attributes
{
    public class AttributeCollection : IEnumerable<object>
    {
        public AttributeCollection(IReadOnlyList<object> methodAttributes, IReadOnlyList<object> classAttributes, IReadOnlyList<object> assemblyAttributes)
        {
            MethodAttributes = methodAttributes;
            ClassAttributes = classAttributes;
            AssemblyAttributes = assemblyAttributes;
        }

        public static AttributeCollection FromMethodInfo(MethodInfo method)
        {
            var methodAttributes = method.GetCustomAttributes(true);
            var classAttributes = method.DeclaringType?.GetCustomAttributes(true) ?? Array.Empty<object>();
            var assemblyAttributes = method.DeclaringType?.Assembly.GetCustomAttributes(true) ?? Array.Empty<object>();

            return new AttributeCollection(methodAttributes, classAttributes, assemblyAttributes);
        }

        public IReadOnlyList<object> MethodAttributes { get; }

        public IReadOnlyList<object> ClassAttributes { get; }

        public IReadOnlyList<object> AssemblyAttributes { get; }

        public T? GetAttribute<T>() where T : class
        {
            return (T?)this.FirstOrDefault(o => o is T);
        }

        public IReadOnlyList<T> GetAttributes<T>() where T : class
        {
            return this.OfType<T>().ToList();
        }

        public IEnumerator<object> GetEnumerator()
        {
            return EnumerateAll().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private IEnumerable<object> EnumerateAll()
        {
            foreach (object methodAttribute in MethodAttributes)
            {
                yield return methodAttribute;
            }

            foreach (object classAttribute in ClassAttributes)
            {
                yield return classAttribute;
            }

            foreach (object assemblyAttribute in AssemblyAttributes)
            {
                yield return assemblyAttribute;
            }
        }
    }
}

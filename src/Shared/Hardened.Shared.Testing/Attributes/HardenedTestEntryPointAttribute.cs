using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing.Attributes
{
    /// <summary>
    /// Used to specify an entry point for a test
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public class HardenedTestEntryPointAttribute : Attribute
    {
        public HardenedTestEntryPointAttribute(Type entryPoint)
        {
            EntryPoint = entryPoint;
        }

        public Type EntryPoint { get; }
    }
}

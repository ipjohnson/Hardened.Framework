using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Application
{
    public interface IEnvironment
    {
        string Name { get; }

        T? Value<T>(string name, T? defaultValue = default);
    }
}

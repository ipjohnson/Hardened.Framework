using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Execution
{
    public class ExecutionRequestParameter : IExecutionRequestParameter
    {
        public ExecutionRequestParameter(string name, int index, Type type)
        {
            Name = name;
            Index = index;
            Type = type;
        }

        public string Name { get; }

        public int Index { get; }

        public Type Type { get; }

    }
}

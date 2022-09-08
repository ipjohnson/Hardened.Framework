using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Attributes
{
    public class ForEnvironmentAttribute
    {
        public ForEnvironmentAttribute(string environment)
        {
            Environment = environment;
        }

        public string Environment { get; }
    }
}

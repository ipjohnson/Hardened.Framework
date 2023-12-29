using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing.Attributes;

public interface IHardenedOrderedAttribute {
    int Order => 10;
}
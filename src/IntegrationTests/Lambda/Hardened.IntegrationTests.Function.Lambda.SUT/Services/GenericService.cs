using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Function.Lambda.SUT.Services
{
    public interface IGenericService<T>
    {

    }

    [Expose(typeof(GenericService<>), Try = true)]
    public class GenericService<T> : IGenericService<T>
    {

    }
}

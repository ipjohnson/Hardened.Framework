using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Services;

public interface INamespaceTestService
{

}

[Expose]
public class NamespaceTestService : INamespaceTestService
{
}

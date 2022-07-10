using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Runtime.Helpers;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String
{
    public abstract class BaseTwoStringTests : BaseHelperTests
    {   
        protected async Task Evaluate(string one, string two, bool result)
        {
            var defaultHelper = new DefaultHelpers();

            var templateHelperFunc = defaultHelper.GetTemplateHelperFactory(Token);

            var templateHelper = templateHelperFunc(null);

            Assert.Equal(result, await templateHelper.Execute(null, one, two));
        }
    }
}

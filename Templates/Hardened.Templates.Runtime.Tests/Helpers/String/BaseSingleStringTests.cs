using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Runtime.Helpers;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String
{
    public abstract class BaseSingleStringTests : BaseHelperTests
    {
        protected async Task Evaluate(string input, string expected)
        {
            var defaultHelper = new DefaultHelpers();

            var templateHelperFunc = defaultHelper.GetTemplateHelperFactory(Token);

            var templateHelper = templateHelperFunc(null);

            Assert.Equal(expected, await templateHelper.Execute(null, input));
        }
    }
}

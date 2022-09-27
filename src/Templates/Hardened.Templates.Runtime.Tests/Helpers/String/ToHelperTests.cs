using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String
{
    public class ToHelperTests : BaseHelperTests
    {
        [Fact]
        public async Task IntToString()
        {
            var helper = GetHelper();

            var result = await helper.Execute(GetExecutionContext(), 123);

            Assert.NotNull(result);
            Assert.Equal("123", result);
        }

        [Fact]
        public async Task IntToStringWithFormat()
        {
            var helper = GetHelper();

            var result = await helper.Execute(GetExecutionContext(), 123, "C");

            Assert.NotNull(result);
            Assert.Equal(123.ToString("C"), result);
        }

        protected override Type TemplateHelperType => typeof(ToHelper);

        protected override string Token => DefaultHelpers.StringHelperToken.To;
    }
}

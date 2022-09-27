using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String
{
    public class ReplaceHelperTests : BaseHelperTests
    {
        [Fact]
        public async Task ReplaceWithString()
        {
            var helper = GetHelper();

            var result =
                await helper.Execute(GetExecutionContext(), "Some Interesting String", "Interesting", "Boring");

            Assert.NotNull(result);
            Assert.Equal("Some Boring String", result);
        }

        [Fact]
        public async Task ReplaceWithInt()
        {
            var helper = GetHelper();

            var result =
                await helper.Execute(GetExecutionContext(), "Some Interesting String", "Interesting", "1");

            Assert.NotNull(result);
            Assert.Equal("Some 1 String", result);
        }

        [Fact]
        public async Task ReplaceWithNull()
        {
            var helper = GetHelper();

            var result =
                await helper.Execute(GetExecutionContext(), "Some Interesting String", "Interesting");

            Assert.NotNull(result);
            Assert.Equal("Some  String", result);
        }

        [Fact]
        public async Task NoStringChange()
        {
            var helper = GetHelper();

            var result =
                await helper.Execute(GetExecutionContext(), "Some Interesting String", null);

            Assert.NotNull(result);
            Assert.Equal("Some Interesting String", result);
        }

        protected override Type TemplateHelperType => typeof(ReplaceHelper);

        protected override string Token => DefaultHelpers.StringHelperToken.Replace;
    }
}

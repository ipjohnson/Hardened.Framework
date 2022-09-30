using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.Collection;
using NSubstitute;
using SimpleFixture.xUnit;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.Collection
{
    public class LookupHelperTests : BaseHelperTests
    {
        private readonly ITemplateExecutionContext _mockExecutionContext = 
            Substitute.For<ITemplateExecutionContext>();
      
        [Theory]
        [AutoData]
        public async void ArrayLookup(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(_mockExecutionContext, new [] { 1, 2, 3 }, 2);

            Assert.NotNull(result);
            Assert.Equal(3, result);
        }
        
        [Theory]
        [AutoData]
        public async void ArrayLookupIndexOutOfRange(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(_mockExecutionContext, new[] { 1, 2, 3 }, -1);

            Assert.Null(result);

            result = await lookupHelper.Execute(_mockExecutionContext, new[] { 1, 2, 3 }, 3);

            Assert.Null(result);
        }

        [Theory]
        [AutoData]
        public async void ListLookup(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(_mockExecutionContext, new List<int> { 1, 2, 3 }, 2);

            Assert.NotNull(result);
            Assert.Equal(3, result);
        }

        [Theory]
        [AutoData]
        public async void ListLookupIndexOutOfRange(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(_mockExecutionContext, new List<int> { 1, 2, 3 }, -1);

            Assert.Null(result);

            result = await lookupHelper.Execute(_mockExecutionContext, new List<int> { 1, 2, 3 }, 3);

            Assert.Null(result);
        }

        [Theory]
        [AutoData]
        public async void DictionaryLookup(LookupHelper lookupHelper)
        {
            var dictionary = new Dictionary<string, int>
            {
                {"key1", 1},
                {"key2", 2},
                {"key3", 3}
            };
            
            var result = await lookupHelper.Execute(_mockExecutionContext, dictionary, "key3");

            Assert.NotNull(result);
            Assert.Equal(3, result);
        }

        [Theory]
        [AutoData]
        public async void DictionaryLookupNotFound(LookupHelper lookupHelper)
        {
            var dictionary = new Dictionary<string, int>
            {
                {"key1", 1},
                {"key2", 2},
                {"key3", 3}
            };

            var result = await lookupHelper.Execute(_mockExecutionContext, dictionary, "key4");

            Assert.Null(result);
        }

        protected override Type TemplateHelperType => typeof(LookupHelper);

        protected override string Token => DefaultHelpers.CollectionHelperTokens.Lookup;
    }
}

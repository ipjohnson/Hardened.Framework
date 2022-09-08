using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.Collection;
using SimpleFixture.xUnit;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.Collection
{
    public class LookupHelperTests : BaseHelperTests
    {
      
        [Theory]
        [AutoData]
        public async void ArrayLookup(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(null, new [] { 1, 2, 3 }, 2);

            Assert.NotNull(result);
            Assert.Equal(3, result);
        }
        
        [Theory]
        [AutoData]
        public async void ArrayLookupIndexOutOfRange(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(null, new[] { 1, 2, 3 }, -1);

            Assert.Null(result);

            result = await lookupHelper.Execute(null, new[] { 1, 2, 3 }, 3);

            Assert.Null(result);
        }

        [Theory]
        [AutoData]
        public async void ListLookup(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(null, new List<int> { 1, 2, 3 }, 2);

            Assert.NotNull(result);
            Assert.Equal(3, result);
        }

        [Theory]
        [AutoData]
        public async void ListLookupIndexOutOfRange(LookupHelper lookupHelper)
        {
            var result = await lookupHelper.Execute(null, new List<int> { 1, 2, 3 }, -1);

            Assert.Null(result);

            result = await lookupHelper.Execute(null, new List<int> { 1, 2, 3 }, 3);

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
            
            var result = await lookupHelper.Execute(null, dictionary, "key3");

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

            var result = await lookupHelper.Execute(null, dictionary, "key4");

            Assert.Null(result);
        }

        protected override Type TemplateHelperType => typeof(LookupHelper);

        protected override string Token => DefaultHelpers.CollectionHelperTokens.Lookup;
    }
}

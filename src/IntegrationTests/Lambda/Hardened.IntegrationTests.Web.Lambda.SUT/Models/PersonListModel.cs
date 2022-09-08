using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.IntegrationTests.Web.Lambda.SUT.Models
{
    public class PersonListModel
    {
        public PersonListModel(string title, IEnumerable<PersonModel> entries)
        {
            Title = title;
            Entries = entries;
        }

        public string Title { get; }

        public IEnumerable<PersonModel> Entries { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public delegate ITemplateHelper TemplateHelperFactory(IServiceProvider serviceProvider);

    public interface ITemplateHelperService
    {
        TemplateHelperFactory LocateHelper(string helperToken);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public interface IStringEscapeService
    {
        bool CanEscapeTemplate(string templateExtension);

        string EscapeString(string? value);
    }
}

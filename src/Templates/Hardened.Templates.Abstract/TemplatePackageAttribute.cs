using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public class TemplatePackageAttribute : Attribute
    {
        public string Extensions { get; set; } = "html";

        public string Token { get; set; } = "{{TOKEN}}";
    }
}

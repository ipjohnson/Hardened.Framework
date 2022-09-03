using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public class LayoutModel
    {
        public LayoutModel(object model, string templateName)
        {
            Model = model;
            TemplateName = templateName;
        }

        public object Model { get; }

        public string TemplateName { get; }

    }
}

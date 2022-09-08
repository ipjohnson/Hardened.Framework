using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public class LayoutModel
    {
        public LayoutModel(object model, TemplateExecutionFunction templateFunc)
        {
            Model = model;
            TemplateFunc = templateFunc;
        }

        public object Model { get; }

        public TemplateExecutionFunction TemplateFunc { get; }

    }
}

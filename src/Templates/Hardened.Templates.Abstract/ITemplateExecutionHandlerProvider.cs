﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{

    public interface ITemplateExecutionHandlerProvider
    {
        ITemplateExecutionService? TemplateExecutionService { get; set; }

        ITemplateExecutionHandler? GetTemplateExecutionHandler(string templateName);
    }
}
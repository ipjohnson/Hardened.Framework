﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{

    public interface ITemplateHelperProvider
    {
        TemplateHelperFactory GetTemplateHelperFactory(string mustacheToken);
    }
}
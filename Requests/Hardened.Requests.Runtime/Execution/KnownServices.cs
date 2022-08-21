﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Execution
{
    public class KnownServices : IKnownServices
    {
        public KnownServices(IContextSerializationService contextSerializationService, IStringConverterService stringConverterService)
        {
            ContextSerializationService = contextSerializationService;
            StringConverterService = stringConverterService;
        }

        public IContextSerializationService ContextSerializationService { get; }

        public IStringConverterService StringConverterService { get; }
    }
}

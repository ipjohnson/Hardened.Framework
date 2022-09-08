﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.PathTokens
{
    public class PathToken
    {
        public PathToken(string tokenName, string tokenValue)
        {
            TokenName = tokenName;
            TokenValue = tokenValue;
        }

        public string TokenName { get; }

        public string TokenValue { get; }
    }
}
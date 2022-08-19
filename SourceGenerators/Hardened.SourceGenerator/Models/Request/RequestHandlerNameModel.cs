using System;
using System.Collections.Generic;
using System.Text;

namespace Hardened.SourceGenerator.Models.Request
{
    public class RequestHandlerNameModel
    {
        public RequestHandlerNameModel(string path, string method)
        {
            Path = path;
            Method = method;
        }

        public string Path { get; }
        
        public string Method { get; }
    }
}

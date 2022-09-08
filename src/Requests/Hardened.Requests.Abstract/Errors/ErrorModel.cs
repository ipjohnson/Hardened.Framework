using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.Errors
{
    public class ErrorModel
    {
        public string Type { get; set; }

        public string Message { get; set; }

        public string Details { get; set; }
    }
}

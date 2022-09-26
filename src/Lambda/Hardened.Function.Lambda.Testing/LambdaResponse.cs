using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Function.Lambda.Testing
{
    public class LambdaResponse
    {
        public LambdaResponse(int status, byte[] payload)
        {
            Status = status;
            Payload = payload;
        }

        public int Status { get; }

        public byte[] Payload { get; }
    }
}

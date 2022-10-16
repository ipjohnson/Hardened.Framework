using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.SqsClient;

public class SqsMessageAttribute : Attribute
{
    public SqsMessageAttribute(string queueName)
    {
        QueueName = queueName;
    }

    public string QueueName { get; }
}

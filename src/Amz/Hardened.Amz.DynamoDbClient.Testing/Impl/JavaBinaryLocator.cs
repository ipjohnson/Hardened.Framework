using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Amz.DynamoDbClient.Testing.Impl;

public static class JavaBinaryLocator
{
    public static string LocateJava()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"C:\Program Files\Java\jdk-16.0.1\bin\java.exe";
        }
        else
        {
            throw new NotImplementedException("Not implemented yet");
        }
    }
}

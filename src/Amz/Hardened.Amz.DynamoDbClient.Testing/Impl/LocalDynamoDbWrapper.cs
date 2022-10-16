using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Amz.DynamoDbClient.Testing.Impl;

public interface ILocalDynamoDbWrapper : IDisposable
{

}
public class LocalDynamoDbWrapper : ILocalDynamoDbWrapper
{
    private Process? _process;

    public LocalDynamoDbWrapper(string ddbJarPath, string? javaPath = null, int port = 8000)
    {
        var currentFolder = GetType().Assembly.Location;

        currentFolder = Path.GetDirectoryName(currentFolder);

        var jarPath = $"{ddbJarPath}/ddb_local_dist";

        if (!Directory.Exists(jarPath))
        {
            throw new Exception("Can't find jar path: " + jarPath);
        }

        var dirPath = Path.GetFullPath(jarPath);
        var fullJarPath = Path.Combine(dirPath, "DynamoDBLocal.jar");

        _process = new Process();
        _process.StartInfo.FileName =
            javaPath ?? JavaBinaryLocator.LocateJava(); // relative path. absolute path works too.
        _process.StartInfo.Arguments =
            $"-Djava.library.path=\"{dirPath}\" -jar \"{fullJarPath}\" -inMemory -port {port}";
        _process.StartInfo.CreateNoWindow = true;
        _process.StartInfo.UseShellExecute = false;
        _process.StartInfo.RedirectStandardOutput = true;
        _process.StartInfo.RedirectStandardError = true;

        _process.OutputDataReceived += (sender, data) => Console.WriteLine(data.Data);
        _process.ErrorDataReceived += (sender, data) => Console.WriteLine(data.Data);
        Console.WriteLine("starting");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        Thread.Sleep(1000);
    }

    public void Dispose()
    {
        var value = _process;

        if (value != null &&
            Interlocked.CompareExchange(ref _process, null, value) == value)
        {
            value.Kill(true);
            value.WaitForExit(5000);
            value.Dispose();
        }
    }
}

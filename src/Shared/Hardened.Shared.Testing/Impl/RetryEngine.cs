using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing.Impl;

public class RetryEngine : IRetryEngine
{
    private ITestContext _context;

    public RetryEngine(ITestContext context)
    {
        _context = context;
    }

    public int Delay { get; set; } = 1000;

    public async Task TillTrue(Func<Task<bool>> testFunc, string description, params object[] parameters)
    {
        var keepRunning = true;
        
        while (keepRunning)
        {
            _context.CancellationRequest.ThrowIfCancellationRequested();

            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            _context.Logger.LogInformation(description, parameters);
            
            try
            {
                keepRunning = !await testFunc();
            }
            catch (Exception exp)
            {
                _context.Logger.LogInformation(exp, "Failed step");   
            }

            if (keepRunning)
            {
                await Task.Delay(1000, _context.CancellationRequest);
            }
        }
    }

    public async Task TillFalse(Func<Task<bool>> testFunc, string description, params object[] parameters)
    {
        var keepRunning = true;
        
        while (keepRunning)
        {
            _context.CancellationRequest.ThrowIfCancellationRequested();

            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            _context.Logger.LogInformation(description, parameters);
            
            try
            {
                keepRunning = await testFunc();
            }
            catch (Exception exp)
            {
                _context.Logger.LogInformation(exp, "Failed step");   
            }

            if (keepRunning)
            {
                await Task.Delay(1000, _context.CancellationRequest);
            }
        }
    }

    public async Task<T> TillValue<T>(Func<Task<T>> testFunc, string description, params object[] parameters)
    {
        while (true)
        {
            _context.CancellationRequest.ThrowIfCancellationRequested();
            
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            _context.Logger.LogInformation(description, parameters);
            
            try
            {
                return await testFunc();
            }
            catch (Exception exp)
            {
                _context.Logger.LogInformation(exp, "Failed step");   
            }

            await Task.Delay(1000, _context.CancellationRequest);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Hardened.Function.Lambda.Runtime.Impl;
using Hardened.Shared.Lambda.Testing;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Function.Lambda.Testing
{
    public static class ILambdaHandlerExtensions
    {

        public static async Task<TResponse> Invoke<TResponse>(this ILambdaHandler<TResponse> tHandler, ILambdaContext? lambdaContext = null)
        {
            using var memoryStreamReservation = tHandler.Provider.GetRequiredService<IMemoryStreamPool>().Get();

            await using var response = await tHandler.Invoke(memoryStreamReservation.Item, 
                lambdaContext ?? ConstructLambda(tHandler.GetType()));

            return (await JsonSerializer.DeserializeAsync<TResponse>(response))!;
        }

        public static Task<TResponse> Invoke<TRequest, TResponse>(
            this ILambdaHandler<TRequest, TResponse> tHandler, TRequest request,
            Action<IDictionary<string,string>> customContextData)
        {
            var customContextDataDictionary = new Dictionary<string, string>();

            customContextData.Invoke(customContextDataDictionary);

            return Invoke(tHandler, request, TestLambdaContext.FromHandlerType(typeof(TRequest), customContextDataDictionary));
        }

        public static async Task<TResponse> Invoke<TRequest, TResponse>(this ILambdaHandler<TRequest,TResponse> tHandler, TRequest request,
            ILambdaContext? lambdaContext = null)
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            using var memoryStreamReservation = tHandler.Provider.GetRequiredService<IMemoryStreamPool>().Get();
            
            JsonSerializer.Serialize(memoryStreamReservation.Item, request, jsonOptions);

            memoryStreamReservation.Item.Position = 0;

            await using var response = await tHandler.Invoke(memoryStreamReservation.Item,
                lambdaContext ?? ConstructLambda(tHandler.GetType()));

            return (await JsonSerializer.DeserializeAsync<TResponse>(response, jsonOptions))!;
        }

        private static ILambdaContext ConstructLambda(Type type)
        {
            return TestLambdaContext.FromHandlerType(type);
        }
    }
}

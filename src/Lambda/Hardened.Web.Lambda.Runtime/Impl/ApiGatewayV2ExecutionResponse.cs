﻿using Amazon.Lambda.APIGatewayEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Headers;

namespace Hardened.Web.Lambda.Runtime.Impl
{
    public class ApiGatewayV2ExecutionResponse : IExecutionResponse
    {
        private readonly APIGatewayHttpApiV2ProxyResponse _proxyResponse;
        private IHeaderCollection? _headerCollection;
        public ApiGatewayV2ExecutionResponse(APIGatewayHttpApiV2ProxyResponse response)
        {
            _proxyResponse = response;
        }

        public object Clone()
        {
            throw new NotImplementedException();
        }

        public string? ContentType { get; set; }

        public object? ResponseValue { get; set; }

        public string? TemplateName { get; set; }

        public int? Status
        {
            get => _proxyResponse.StatusCode;
            set => _proxyResponse.StatusCode = value.GetValueOrDefault(200) ;
        }
        
        public bool ShouldCompress { get; set; }

        public Stream? Body { get; set; }

        public IHeaderCollection Headers =>
            _headerCollection ??= new HeaderCollectionStringDictionary(_proxyResponse.Headers);

        public Exception? ExceptionValue { get; set; }

        public bool ResponseStarted => Body?.Position > 0;

        public bool IsBinary { get; set; }
    }
}
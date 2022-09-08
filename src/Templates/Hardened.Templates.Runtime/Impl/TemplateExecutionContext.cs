using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl
{
    public class TemplateExecutionContext : ITemplateExecutionContext
    {
        private Dictionary<string, object>? _values;
        
        public TemplateExecutionContext(
            string templateExtension, 
            IServiceProvider requestServiceProvider, 
            object objectValue,
            IInternalTemplateServices templateServices,
            ITemplateExecutionService executionService, 
            IStringEscapeService stringEscapeService,
            ITemplateOutputWriter writer, 
            ITemplateExecutionContext? parentContext, 
            IExecutionContext? executionContext)
        {
            RequestServiceProvider = requestServiceProvider ?? throw new ArgumentNullException(nameof(requestServiceProvider));
            ObjectValue = objectValue;
            TemplateServices = templateServices;
            Writer = writer;
            ParentContext = parentContext;
            ExecutionContext = executionContext;
            StringEscapeService = stringEscapeService;
            ExecutionService = executionService;
            TemplateExtension = 
                templateExtension ?? throw new ArgumentNullException(nameof(templateExtension));
        }
        
        public ITemplateOutputWriter Writer { get; }

        public ITemplateExecutionService ExecutionService { get; }

        public IInternalTemplateServices TemplateServices { get; }

        public IStringEscapeService StringEscapeService { get; }

        public string TemplateExtension { get; }
        
        public IServiceProvider RequestServiceProvider { get; }

        public ITemplateExecutionContext? ParentContext { get; }

        public IExecutionContext? ExecutionContext { get; }

        public object ObjectValue { get; }

        public void SetCustomValue(string key, object value)
        {
            if (_values == null)
            {
                _values = new Dictionary<string, object>();
            }

            _values[key] = value;
        }

        public SafeString GetEscapedString(object value, string propertyName = "", string formattingString = "")
        {
            return new SafeString(
                StringEscapeService.EscapeString(
                TemplateServices.DataFormattingService.FormatData(this, propertyName, value, formattingString)?.ToString() ?? ""));
        }

        public object? GetCustomValue(string key)
        {
            if (_values != null && _values.ContainsKey(key))
            {
                return _values[key];
            }

            if (ParentContext != null)
            {
                return ParentContext.GetCustomValue(key);
            }

            return null;
        }
    }
}

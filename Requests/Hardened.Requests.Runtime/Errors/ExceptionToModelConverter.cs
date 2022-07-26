﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Requests.Runtime.Errors
{
    public class ExceptionToModelConverter : IExceptionToModelConverter
    {
        public (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp)
        {
            var statusCode = 500;
            var model = new ErrorModel
            {
                Type =  exp.GetType().Name, 
                Message = exp.Message
            };

            if (exp.GetType().Name.Contains("Validation") || exp.GetType().Name.Contains("Bad"))
            {
                statusCode = 400;
            }
            
            return (statusCode, model);
        }
    }
}

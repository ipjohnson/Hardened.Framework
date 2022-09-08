﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public interface ITemplateOutputWriter
    {
        /// <summary>
        /// Write string to output, it will be escaped 
        /// </summary>
        /// <param name="text"></param>
        void Write(object? text);

        /// <summary>
        /// Write string to output, no escaping will be done
        /// </summary>
        /// <param name="text"></param>
        void WriteRaw(object? text);

        /// <summary>
        /// Flush Writer
        /// </summary>
        /// <returns></returns>
        Task FlushWriter();

        IStringEscapeService? EscapeService { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public enum TemplateHelperLifecycle
    {
        /// <summary>
        /// Only one instance per template environment (default)
        /// </summary>
        Singleton,

        /// <summary>
        /// One instance is created per scope
        /// </summary>
        Scoped,

        /// <summary>
        /// One instance is created per template generation
        /// </summary>
        Transient,
    }

    /// <summary>
    /// Attribute helper classes to be used in template {{$MustacheToken args}}
    /// </summary>
    public class TemplateHelperAttribute : Attribute
    {
        public TemplateHelperAttribute(string token, TemplateHelperLifecycle lifecycle = TemplateHelperLifecycle.Singleton)
        {
            MustacheToken = token;
            Lifecycle = lifecycle;
        }

        /// <summary>
        /// name of token to used in template {{$MustacheToken args}}
        /// </summary>
        public string MustacheToken { get; }

        /// <summary>
        /// Lifecycle for the template helper
        /// </summary>
        public TemplateHelperLifecycle Lifecycle { get; }
    }
}

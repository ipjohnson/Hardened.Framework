﻿using Hardened.SourceGenerator.Module;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Generator;
using Hardened.SourceGenerator.Web;

namespace Hardened.Web.SourceGenerator
{
    [Generator]
    public class WebLibrarySourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(false)
            ).WithComparer(new EntryPointSelector.Comparer());

            WebIncrementalGenerator.Setup(context, applicationModel);
        }
    }
}

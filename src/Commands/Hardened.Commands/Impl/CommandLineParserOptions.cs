using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Commands.Impl;

[ConfigurationModel]
public partial class CommandLineParserOptions {
    private string _optionPrefix = "--";
}
namespace Hardened.Commands.Impl;

public interface ICommandBinder<T> {
    void Bind(IReadOnlyDictionary<string, string[]> data, T model);
}
namespace Hardened.Commands;

public interface ICommandHandler<in T> {
    Task<int> Handle(T value);
}
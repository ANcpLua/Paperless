namespace Contract.Logger;

public interface IOperationLogger
{
    Task LogOperation(LogOperationAttribute attribute, string methodName, object?[] parameters);
    Task LogOperationError(LogOperationAttribute attribute, string methodName, Exception ex);
}
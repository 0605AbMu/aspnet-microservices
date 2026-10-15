namespace Microservices.Common.Exceptions;

/// <summary>
/// Base application exception. Should be extended by strongly-typed domain exceptions.
/// Args are returned in the response envelope's data field for client-side i18n localization.
/// </summary>
public abstract class AppException(
    string message,
    ErrorType errorType = ErrorType.Failure,
    object?[]? args = null,
    string? logDetails = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ErrorType ErrorType { get; } = errorType;
    public object?[]? Args { get; } = args;
    public string? LogDetails { get; } = logDetails;
}

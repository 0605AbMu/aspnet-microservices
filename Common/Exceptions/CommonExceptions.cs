namespace Microservices.Common.Exceptions;

public class NotFoundException(
    string message = "resource_not_found",
    object?[]? args = null,
    string? logDetails = null,
    Exception? innerException = null)
    : AppException(message, ErrorType.NotFound, args, logDetails, innerException);

public class ConflictException(
    string message = "resource_conflict",
    object?[]? args = null,
    string? logDetails = null,
    Exception? innerException = null)
    : AppException(message, ErrorType.Conflict, args, logDetails, innerException);

public class ForbiddenException(
    string message = "forbidden",
    object?[]? args = null,
    string? logDetails = null,
    Exception? innerException = null)
    : AppException(message, ErrorType.Forbidden, args, logDetails, innerException);

public class UnauthorizedException(
    string message = "unauthorized",
    object?[]? args = null,
    string? logDetails = null,
    Exception? innerException = null)
    : AppException(message, ErrorType.Unauthorized, args, logDetails, innerException);

public class ValidationException(
    string message = "validation_error",
    object?[]? args = null,
    string? logDetails = null,
    IDictionary<string, string[]>? errors = null,
    Exception? innerException = null)
    : AppException(message, ErrorType.Validation, args, logDetails, innerException)
{
    public IDictionary<string, string[]>? Errors { get; } = errors;
}

public class EntityNotFoundException(string entityName, object? id = null)
    : NotFoundException($"{entityName.ToLowerInvariant()}_not_found", id != null ? [id] : null);

public class EntityAlreadyExistsException(string entityName, object? key = null)
    : ConflictException($"{entityName.ToLowerInvariant()}_already_exists", key != null ? [key] : null);

public class ConcurrencyConflictException(string? entityName = null, object? id = null)
    : ConflictException("concurrency_conflict", entityName != null && id != null ? [entityName, id] : null);

using ProtoBuf;

namespace Microservices.Common.Models;

[ProtoContract]
public class ApiResponse<T>
{
    [ProtoMember(1)]
    public bool Success { get; init; }

    [ProtoMember(2)]
    public int Code { get; init; }

    [ProtoMember(3)]
    public string? Message { get; init; }

    [ProtoMember(4)]
    public T? Data { get; init; }

    [ProtoMember(5)]
    public string? CorrelationId { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null, int code = 200, string? correlationId = null) =>
        new()
        {
            Success = true,
            Code = code,
            Message = message,
            Data = data,
            CorrelationId = correlationId
        };

    public static ApiResponse<T> Fail(string message, int code = 400, T? data = default, string? correlationId = null) =>
        new()
        {
            Success = false,
            Code = code,
            Message = message,
            Data = data,
            CorrelationId = correlationId
        };
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null, int code = 200, string? correlationId = null) =>
        ApiResponse<T>.Ok(data, message, code, correlationId);

    public static ApiResponse<object?> Fail(string message, int code = 400, object? data = null, string? correlationId = null) =>
        ApiResponse<object?>.Fail(message, code, data, correlationId);
}

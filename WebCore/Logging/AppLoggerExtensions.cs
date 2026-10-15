using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Microservices.WebCore.Logging;

public static class AppLogContext
{
    private static string _currentServiceName = "microservice";

    public static string CurrentServiceName
    {
        get => _currentServiceName;
        set => _currentServiceName = !string.IsNullOrWhiteSpace(value) ? value : _currentServiceName;
    }
}

public static class AppLoggerExtensions
{
    public static void LogWithMethod(
        this ILogger logger,
        LogLevel logLevel,
        string message,
        [CallerMemberName] string methodName = "",
        params object?[] args)
    {
        using (LogContext.PushProperty("MethodName", methodName))
        {
            logger.Log(logLevel, message, args);
        }
    }

    public static void LogMethodInformation(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        params object?[] args)
    {
        LogWithMethod(logger, LogLevel.Information, message, methodName, args);
    }

    public static void LogMethodWarning(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        params object?[] args)
    {
        LogWithMethod(logger, LogLevel.Warning, message, methodName, args);
    }

    public static void LogMethodError(
        this ILogger logger,
        Exception? exception,
        string message,
        [CallerMemberName] string methodName = "",
        params object?[] args)
    {
        using (LogContext.PushProperty("MethodName", methodName))
        {
            logger.LogError(exception, message, args);
        }
    }
}

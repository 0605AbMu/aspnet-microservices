using System.IO;
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

/// <summary>
/// High-performance, zero-allocation compiler-generated logging extensions.
/// Automatically captures MethodName, ClassName, and LineNumber at compile-time via Caller attributes.
/// Completely free of runtime StackTrace overhead.
/// </summary>
public static class AppLoggerExtensions
{
    private static void PushCallerContextAndLog(
        ILogger logger,
        LogLevel level,
        Exception? exception,
        string message,
        string methodName,
        string sourceFilePath,
        int sourceLineNumber,
        object?[]? args)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var className = Path.GetFileNameWithoutExtension(sourceFilePath);
        using (LogContext.PushProperty("MethodName", methodName))
        using (LogContext.PushProperty("ClassName", className))
        using (LogContext.PushProperty("CallerClass", className))
        using (LogContext.PushProperty("LineNumber", sourceLineNumber))
        {
            if (exception is not null)
            {
                if (args is { Length: > 0 })
                    logger.LogError(exception, message, args);
                else
                    logger.LogError(exception, message);
            }
            else
            {
                if (args is { Length: > 0 })
                    logger.Log(level, message, args);
                else
                    logger.Log(level, message);
            }
        }
    }

    #region LogInfo

    public static void LogInfo(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Information, null, message, methodName, sourceFilePath, sourceLineNumber, null);
    }

    public static void LogInfo<T0>(
        this ILogger logger,
        string message,
        T0 arg0,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;
        PushCallerContextAndLog(logger, LogLevel.Information, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0]);
    }

    public static void LogInfo<T0, T1>(
        this ILogger logger,
        string message,
        T0 arg0,
        T1 arg1,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;
        PushCallerContextAndLog(logger, LogLevel.Information, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1]);
    }

    public static void LogInfo<T0, T1, T2>(
        this ILogger logger,
        string message,
        T0 arg0,
        T1 arg1,
        T2 arg2,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Information)) return;
        PushCallerContextAndLog(logger, LogLevel.Information, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1, arg2]);
    }

    public static void LogInfoArgs(
        this ILogger logger,
        string message,
        object?[] args,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Information, null, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    #endregion

    #region LogWarn

    public static void LogWarn(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Warning, null, message, methodName, sourceFilePath, sourceLineNumber, null);
    }

    public static void LogWarn<T0>(
        this ILogger logger,
        string message,
        T0 arg0,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Warning)) return;
        PushCallerContextAndLog(logger, LogLevel.Warning, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0]);
    }

    public static void LogWarn<T0, T1>(
        this ILogger logger,
        string message,
        T0 arg0,
        T1 arg1,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Warning)) return;
        PushCallerContextAndLog(logger, LogLevel.Warning, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1]);
    }

    public static void LogWarn<T0, T1, T2>(
        this ILogger logger,
        string message,
        T0 arg0,
        T1 arg1,
        T2 arg2,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Warning)) return;
        PushCallerContextAndLog(logger, LogLevel.Warning, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1, arg2]);
    }

    public static void LogWarnArgs(
        this ILogger logger,
        string message,
        object?[] args,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Warning, null, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    #endregion

    #region LogDebug

    public static void LogDebug(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Debug, null, message, methodName, sourceFilePath, sourceLineNumber, null);
    }

    public static void LogDebug<T0>(
        this ILogger logger,
        string message,
        T0 arg0,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        PushCallerContextAndLog(logger, LogLevel.Debug, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0]);
    }

    public static void LogDebug<T0, T1>(
        this ILogger logger,
        string message,
        T0 arg0,
        T1 arg1,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        PushCallerContextAndLog(logger, LogLevel.Debug, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1]);
    }

    public static void LogDebug<T0, T1, T2>(
        this ILogger logger,
        string message,
        T0 arg0,
        T1 arg1,
        T2 arg2,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Debug)) return;
        PushCallerContextAndLog(logger, LogLevel.Debug, null, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1, arg2]);
    }

    #endregion

    #region LogError

    public static void LogError(
        this ILogger logger,
        Exception? exception,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Error, exception, message, methodName, sourceFilePath, sourceLineNumber, null);
    }

    public static void LogError<T0>(
        this ILogger logger,
        Exception? exception,
        string message,
        T0 arg0,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Error)) return;
        PushCallerContextAndLog(logger, LogLevel.Error, exception, message, methodName, sourceFilePath, sourceLineNumber, [arg0]);
    }

    public static void LogError<T0, T1>(
        this ILogger logger,
        Exception? exception,
        string message,
        T0 arg0,
        T1 arg1,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Error)) return;
        PushCallerContextAndLog(logger, LogLevel.Error, exception, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1]);
    }

    public static void LogError<T0, T1, T2>(
        this ILogger logger,
        Exception? exception,
        string message,
        T0 arg0,
        T1 arg1,
        T2 arg2,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (!logger.IsEnabled(LogLevel.Error)) return;
        PushCallerContextAndLog(logger, LogLevel.Error, exception, message, methodName, sourceFilePath, sourceLineNumber, [arg0, arg1, arg2]);
    }

    public static void LogErrorArgs(
        this ILogger logger,
        Exception? exception,
        string message,
        object?[] args,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        PushCallerContextAndLog(logger, LogLevel.Error, exception, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    #endregion

    #region Legacy Compatibility Methods

    public static void LogWithCaller(
        this ILogger logger,
        LogLevel logLevel,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        params object?[] args)
    {
        PushCallerContextAndLog(logger, logLevel, null, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    public static void LogWithMethod(
        this ILogger logger,
        LogLevel logLevel,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        params object?[] args)
    {
        PushCallerContextAndLog(logger, logLevel, null, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    public static void LogMethodInformation(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        params object?[] args)
    {
        PushCallerContextAndLog(logger, LogLevel.Information, null, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    public static void LogMethodWarning(
        this ILogger logger,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        params object?[] args)
    {
        PushCallerContextAndLog(logger, LogLevel.Warning, null, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    public static void LogMethodError(
        this ILogger logger,
        Exception? exception,
        string message,
        [CallerMemberName] string methodName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        params object?[] args)
    {
        PushCallerContextAndLog(logger, LogLevel.Error, exception, message, methodName, sourceFilePath, sourceLineNumber, args);
    }

    #endregion
}

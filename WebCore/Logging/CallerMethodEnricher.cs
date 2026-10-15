using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Microservices.WebCore.Logging;

/// <summary>
/// Serilog enricher that automatically extracts and attaches the calling MethodName
/// and CallerClass to every log event if not already present in the LogContext.
/// </summary>
public class CallerMethodEnricher : ILogEventEnricher
{
    private static readonly Regex AsyncMethodRegex = new(@"<([^>]+)>", RegexOptions.Compiled);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.ContainsKey("MethodName"))
            return;

        try
        {
            var stackTrace = new StackTrace(skipFrames: 3, fNeedFileInfo: false);
            for (var i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                var method = frame?.GetMethod();
                if (method is null) continue;

                var declaringType = method.DeclaringType;
                if (declaringType is null) continue;

                var ns = declaringType.Namespace ?? string.Empty;
                if (ns.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase) ||
                    ns.StartsWith("Microsoft.Extensions.Logging", StringComparison.OrdinalIgnoreCase) ||
                    ns.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.OrdinalIgnoreCase) ||
                    ns.StartsWith("System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                    ns.StartsWith("System.Threading", StringComparison.OrdinalIgnoreCase) ||
                    declaringType.Assembly == typeof(CallerMethodEnricher).Assembly && declaringType.Name.Contains("Logging"))
                {
                    continue;
                }

                var methodName = method.Name;

                if (declaringType.IsDefined(typeof(CompilerGeneratedAttribute), false) && declaringType.DeclaringType is not null)
                {
                    var match = AsyncMethodRegex.Match(declaringType.Name);
                    if (match.Success)
                    {
                        methodName = match.Groups[1].Value;
                    }
                }

                var className = declaringType.DeclaringType?.Name ?? declaringType.Name;

                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MethodName", methodName));
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CallerClass", className));
                return;
            }
        }
        catch
        {
            // Logging enrichers must never throw under any circumstances
        }
    }
}

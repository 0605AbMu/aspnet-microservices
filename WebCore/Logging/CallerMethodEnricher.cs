using Serilog.Core;
using Serilog.Events;

namespace Microservices.WebCore.Logging;

/// <summary>
/// Serilog enricher that attaches default properties when not populated at compile time.
/// 100% StackTrace-free, zero runtime overhead.
/// </summary>
public class CallerMethodEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // 1. ClassName / CallerClass (defaults to SourceContext short name if absent)
        if (!logEvent.Properties.ContainsKey("ClassName"))
        {
            var className = "-";
            if (logEvent.Properties.TryGetValue("SourceContext", out var sc) &&
                sc is ScalarValue { Value: string scStr } && !string.IsNullOrWhiteSpace(scStr))
            {
                var lastDot = scStr.LastIndexOf('.');
                className = lastDot >= 0 ? scStr.Substring(lastDot + 1) : scStr;
            }

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClassName", className));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CallerClass", className));
        }

        // 2. MethodName
        if (!logEvent.Properties.ContainsKey("MethodName"))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MethodName", "-"));
        }

        // 3. LineNumber
        if (!logEvent.Properties.ContainsKey("LineNumber"))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("LineNumber", 0));
        }
    }
}

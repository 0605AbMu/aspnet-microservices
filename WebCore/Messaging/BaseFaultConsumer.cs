using MassTransit;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Messaging;

/// <summary>
/// Standard base consumer for dead-letter / poisoned message handling.
/// Subscribes to MassTransit Fault&lt;TMessage&gt; published after all retry policies are exhausted.
/// </summary>
public abstract class BaseFaultConsumer<TMessage>(ILogger logger) : IConsumer<Fault<TMessage>>
    where TMessage : class
{
    public virtual async Task Consume(ConsumeContext<Fault<TMessage>> context)
    {
        var fault = context.Message;
        var exceptions = fault.Exceptions != null && fault.Exceptions.Length > 0
            ? string.Join("; ", fault.Exceptions.Select(e => $"{e.ExceptionType}: {e.Message}"))
            : "No exception details";

        logger.LogError(
            "DeadLetter/Fault message encountered: MessageType={MessageType}, FaultId={FaultId}, CorrelationId={CorrelationId}, Exceptions={Exceptions}",
            typeof(TMessage).Name,
            fault.FaultId,
            context.CorrelationId,
            exceptions);

        await OnFaultAsync(context);
    }

    /// <summary>
    /// Optional hook to perform domain-specific compensation, dead-letter storage, or alerting.
    /// </summary>
    protected virtual Task OnFaultAsync(ConsumeContext<Fault<TMessage>> context) => Task.CompletedTask;
}

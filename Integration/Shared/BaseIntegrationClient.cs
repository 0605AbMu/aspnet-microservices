using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Microservices.Integration.Shared;

/// <summary>
/// Base client for outbound external system integrations with graceful degradation (PendingSync fallback).
/// </summary>
public abstract class BaseIntegrationClient(HttpClient httpClient, ILogger logger, string serviceName)
{
    protected static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    protected HttpClient HttpClient { get; } = httpClient;
    protected ILogger Logger { get; } = logger;
    protected string ServiceName { get; } = serviceName;

    protected async Task<TResponse?> GetOrDefaultAsync<TResponse>(
        string url,
        string operationContext,
        CancellationToken ct = default)
    {
        try
        {
            return await HttpClient.GetFromJsonAsync<TResponse>(url, JsonOpts, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "{ServiceName} unavailable for {OperationContext}. PendingSync fallback.", ServiceName, operationContext);
            return default;
        }
    }

    protected async Task<TResponse?> PostOrDefaultAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        string operationContext,
        CancellationToken ct = default)
    {
        try
        {
            var response = await HttpClient.PostAsJsonAsync(url, request, JsonOpts, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOpts, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "{ServiceName} unavailable for {OperationContext}. PendingSync fallback.", ServiceName, operationContext);
            return default;
        }
    }
}

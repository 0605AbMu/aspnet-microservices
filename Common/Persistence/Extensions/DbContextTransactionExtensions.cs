using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microservices.Common.Persistence.Extensions;

/// <summary>
/// Extension methods for safe, re-entrant, and execution-strategy-aware database transactions.
/// Automatically detects ambient or outer transactions to prevent nested transaction errors.
/// Compatible with PostgreSQL EnableRetryOnFailure (NpgsqlRetryingExecutionStrategy).
/// </summary>
public static class DbContextTransactionExtensions
{
    /// <summary>
    /// Executes an async action within an atomic database transaction.
    /// If an outer transaction already exists, participates in it without creating a nested transaction.
    /// </summary>
    public static async Task Transactional(
        this DbContext dbContext,
        Func<Task> action,
        CancellationToken ct = default)
    {
        await dbContext.Transactional(
            action,
            onCommit: tx => tx.CommitAsync(ct),
            onRollback: tx => tx.RollbackAsync(ct),
            ct: ct);
    }

    /// <summary>
    /// Executes an async action within an atomic database transaction with custom onCommit / onRollback hooks.
    /// </summary>
    public static async Task Transactional(
        this DbContext dbContext,
        Func<Task> action,
        Func<IDbContextTransaction, Task> onCommit,
        Func<IDbContextTransaction, Task> onRollback,
        CancellationToken ct = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                await action();
                await onCommit(transaction);
            }
            catch
            {
                await onRollback(transaction);
                throw;
            }
        });
    }

    /// <summary>
    /// Executes an async function that returns a value within an atomic database transaction.
    /// If an outer transaction already exists, participates in it without creating a nested transaction.
    /// </summary>
    public static async Task<TResult> Transactional<TResult>(
        this DbContext dbContext,
        Func<Task<TResult>> action,
        CancellationToken ct = default)
    {
        return await dbContext.Transactional(
            action,
            onCommit: tx => tx.CommitAsync(ct),
            onRollback: tx => tx.RollbackAsync(ct),
            ct: ct);
    }

    /// <summary>
    /// Executes an async function that returns a value within an atomic database transaction with custom onCommit / onRollback hooks.
    /// </summary>
    public static async Task<TResult> Transactional<TResult>(
        this DbContext dbContext,
        Func<Task<TResult>> action,
        Func<IDbContextTransaction, Task> onCommit,
        Func<IDbContextTransaction, Task> onRollback,
        CancellationToken ct = default)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await action();
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await action();
                await onCommit(transaction);
                return result;
            }
            catch
            {
                await onRollback(transaction);
                throw;
            }
        });
    }
}

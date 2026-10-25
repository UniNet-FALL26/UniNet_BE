using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

// Each instance supervises its own live sockets; no shared connection registry is required.
public sealed class AccountSessionConnections(IServiceScopeFactory scopes, ILogger<AccountSessionConnections> logger) : BackgroundService, IHubFilter
{
    private readonly ConcurrentDictionary<string, HubCallerContext> connections = new();
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!await AccountTokenValidation.ValidateAsync(context.Context.User, context.ServiceProvider.GetRequiredService<UniNetDbContext>(), context.Context.ConnectionAborted)) {
            context.Context.Abort(); throw new HubException("ACCOUNT_SESSION_REVOKED");
        }
        return await next(context);
    }
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (!await AccountTokenValidation.ValidateAsync(context.Context.User, context.ServiceProvider.GetRequiredService<UniNetDbContext>(), context.Context.ConnectionAborted)) { context.Context.Abort(); return; }
        connections[context.Context.ConnectionId] = context.Context;
        try { await next(context); } catch { connections.TryRemove(context.Context.ConnectionId, out _); throw; }
    }
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? error, Func<HubLifetimeContext, Exception?, Task> next)
    {
        connections.TryRemove(context.Context.ConnectionId, out _); await next(context, error);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            if (connections.IsEmpty) continue;
            try {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                var snapshot = connections.ToArray();
                var ids = snapshot.Select(pair => AccountTokenValidation.TrySession(pair.Value.User, out var id, out _) ? id : Guid.Empty).Distinct().ToArray();
                var accounts = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Status, a.SessionVersion }).ToDictionaryAsync(a => a.Id, stoppingToken);
                foreach (var pair in snapshot) {
                    if (pair.Value.ConnectionAborted.IsCancellationRequested || !AccountTokenValidation.TrySession(pair.Value.User, out var id, out var version) || !accounts.TryGetValue(id, out var account) || account.Status != AccountStatus.Active || account.SessionVersion != version) {
                        pair.Value.Abort(); connections.TryRemove(pair.Key, out _);
                    }
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Could not check live account sessions; retrying."); }
        }
    }
}

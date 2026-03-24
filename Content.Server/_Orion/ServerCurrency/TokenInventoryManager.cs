using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Robust.Shared.Network;

namespace Content.Server._Orion.ServerCurrency;

public sealed class TokenInventoryManager
{
    [Dependency] private readonly IServerDbManager _db = default!;

    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly ConcurrentDictionary<NetUserId, SemaphoreSlim> _locks = new();

    public TokenInventoryManager()
    {
        IoCManager.InjectDependencies(this);
    }

    public Dictionary<string, int> GetInventory(NetUserId userId)
    {
        var json = Task.Run(() => _db.GetTokenInventoryJson(userId)).GetAwaiter().GetResult();
        return DeserializeInventory(json);
    }

    public bool TryAddToken(NetUserId userId, string tokenId)
    {
        return WithUserLock(userId,
            inventory =>
        {
            inventory[tokenId] = inventory.GetValueOrDefault(tokenId) + 1;
            return true;
        });
    }

    public bool TryConsumeToken(NetUserId userId, string tokenId)
    {
        return WithUserLock(userId,
            inventory =>
        {
            if (!inventory.TryGetValue(tokenId, out var amount) || amount <= 0)
                return false;

            if (amount == 1)
                inventory.Remove(tokenId);
            else
                inventory[tokenId] = amount - 1;

            return true;
        });
    }

    private bool WithUserLock(NetUserId userId, Func<Dictionary<string, int>, bool> callback)
    {
        var sem = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        sem.Wait();

        try
        {
            var inventory = GetInventory(userId);
            var result = callback(inventory);
            if (!result)
                return false;

            var json = JsonSerializer.Serialize(inventory, JsonOptions);
            Task.Run(() => _db.SetTokenInventoryJson(userId, json)).GetAwaiter().GetResult();
            return true;
        }
        finally
        {
            sem.Release();
        }
    }

    private static Dictionary<string, int> DeserializeInventory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, int>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json, JsonOptions) ?? new Dictionary<string, int>();
        }
        catch
        {
            return new Dictionary<string, int>();
        }
    }
}

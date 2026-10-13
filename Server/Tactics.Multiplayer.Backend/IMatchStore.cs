namespace Tactics.Multiplayer.Backend;

public sealed record StoredMatch(string Json, string WriteLock);

// A read lock identifies exactly the JSON revision read. A false CAS means no write
// took place; ambiguous network/store failures must throw, never return false.
public interface IMatchStore
{
    Task<StoredMatch> ReadAsync(string matchId);
    Task<bool> CreateAsync(string matchId, string json);
    Task<bool> CompareExchangeAsync(string matchId, string expectedWriteLock, string json);
}

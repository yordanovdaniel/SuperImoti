using System.Threading.Channels;

namespace SuperImoti.Web.Data.Import;

/// <summary>
/// Shared between the admin page and the background worker: queues import requests and
/// keeps the progress of the current run so pages can show it.
/// </summary>
public sealed class ImportCoordinator
{
    // Holds at most one pending request; extra requests while one is pending are dropped.
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly Lock _gate = new();
    private ImportSnapshot _snapshot = ImportSnapshot.Idle;

    public ChannelReader<bool> Requests => _requests.Reader;

    /// <summary>Asks the background worker to run an import. Returns false if one is already running.</summary>
    public bool TryRequestRun()
    {
        lock (_gate)
        {
            if (_snapshot.IsRunning)
            {
                return false;
            }
        }

        return _requests.Writer.TryWrite(true);
    }

    public ImportSnapshot Snapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public void MarkStarted()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsRunning = true,
                Stage = "Подготовка",
                Processed = 0,
                Total = 0,
                StartedUtc = DateTime.UtcNow,
                Error = null,
            };
        }
    }

    public void Report(string stage, int processed, int total)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { Stage = stage, Processed = processed, Total = total };
        }
    }

    public void MarkCompleted(ImportResult result)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { IsRunning = false, FinishedUtc = DateTime.UtcNow, LastResult = result };
        }
    }

    public void MarkFailed(string error)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { IsRunning = false, FinishedUtc = DateTime.UtcNow, Error = error };
        }
    }
}

public sealed record ImportSnapshot(
    bool IsRunning,
    string Stage,
    int Processed,
    int Total,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    ImportResult? LastResult,
    string? Error)
{
    public static readonly ImportSnapshot Idle = new(false, string.Empty, 0, 0, null, null, null, null);

    public int Percent => Total == 0 ? 0 : (int)(Processed * 100L / Total);
}

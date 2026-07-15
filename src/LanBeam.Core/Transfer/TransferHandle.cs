using LanBeam.Core.Protocol;

namespace LanBeam.Core.Transfer;

public enum TransferDirection { Send, Receive }

public enum TransferState
{
    Connecting,
    Pairing,
    WaitingApproval,
    Transferring,
    Verifying,
    Completed,
    Rejected,
    Cancelled,
    Failed,
}

/// <summary>UI'nın izlediği canlı transfer durumu (her iki yön için ortak).</summary>
public sealed class TransferHandle
{
    private long _bytesTransferred;
    private readonly CancellationTokenSource _cts = new();

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required TransferDirection Direction { get; init; }
    public required string DisplayName { get; init; }
    public required string PeerName { get; init; }
    public long TotalBytes { get; internal set; }
    public int FileCount { get; internal set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public TransferState State { get; private set; } = TransferState.Connecting;
    public string? FailureReason { get; private set; }

    /// <summary>Durum değişince tetiklenir (herhangi bir iş parçacığından).</summary>
    public event Action<TransferHandle>? Changed;

    public long BytesTransferred => Interlocked.Read(ref _bytesTransferred);

    internal CancellationToken CancellationToken => _cts.Token;

    public bool IsFinished => State is TransferState.Completed or TransferState.Rejected
        or TransferState.Cancelled or TransferState.Failed;

    public void Cancel()
    {
        if (!IsFinished)
            _cts.Cancel();
    }

    internal void AddBytes(long count) => Interlocked.Add(ref _bytesTransferred, count);

    internal void SetBytes(long count) => Interlocked.Exchange(ref _bytesTransferred, count);

    internal void SetState(TransferState state, string? failureReason = null)
    {
        if (IsFinished) return;
        State = state;
        FailureReason = failureReason;
        Changed?.Invoke(this);
    }

    internal void NotifyProgress() => Changed?.Invoke(this);
}

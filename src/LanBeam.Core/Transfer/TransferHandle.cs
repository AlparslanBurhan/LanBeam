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
    /// <summary>Bağlantı koptu; gönderen kendiliğinden yeniden bağlanmayı deniyor.</summary>
    Reconnecting,
    /// <summary>Gönderende: Resume() ile sürer. Alıcıda: bu oturum bitti, gönderen devam edince yenisi açılır.</summary>
    Paused,
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
    private readonly object _gate = new();
    private CancellationTokenSource? _attemptCts;
    private TaskCompletionSource? _resumeSignal;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required TransferDirection Direction { get; init; }
    public required string DisplayName { get; init; }
    public required string PeerName { get; init; }
    public long TotalBytes { get; internal set; }
    public int FileCount { get; internal set; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <summary>Aynı gönderenden aynı içeriğin kimliği; alıcı UI'sı devam eden oturumu eskisinin yerine koyar.</summary>
    public string? ContentKey { get; internal set; }

    public TransferState State { get; private set; } = TransferState.Connecting;
    public string? FailureReason { get; private set; }

    /// <summary>Durum değişince tetiklenir (herhangi bir iş parçacığından).</summary>
    public event Action<TransferHandle>? Changed;

    public long BytesTransferred => Interlocked.Read(ref _bytesTransferred);

    internal CancellationToken CancellationToken => _cts.Token;

    public bool IsFinished => State is TransferState.Completed or TransferState.Rejected
        or TransferState.Cancelled or TransferState.Failed;

    public bool CanPause => Direction == TransferDirection.Send && State == TransferState.Transferring;
    public bool CanResume => Direction == TransferDirection.Send && State == TransferState.Paused;

    /// <summary>Alıcıdaki duraklatılmış oturum zaten kapanmıştır; iptal edilecek bir şey yok.</summary>
    public bool CanCancel => !IsFinished && !(Direction == TransferDirection.Receive && State == TransferState.Paused);

    /// <summary>Sistem/ekran uykusu engellenmeli mi?</summary>
    internal bool IsBusy => !IsFinished && State != TransferState.Paused;

    public void Cancel()
    {
        if (!IsFinished)
            _cts.Cancel();
    }

    /// <summary>Gönderimi durdurur: bağlantılar kapanır, alıcı kalan parçaları resume için saklar.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (CanPause)
                _attemptCts?.Cancel();
        }
    }

    public void Resume() => _resumeSignal?.TrySetResult();

    /// <summary>Gönderimin bir bağlantı denemesi için token: kullanıcı iptali ya da Pause() keser.</summary>
    internal CancellationToken BeginAttempt()
    {
        lock (_gate)
        {
            _attemptCts?.Dispose();
            _attemptCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            return _attemptCts.Token;
        }
    }

    /// <summary>Duraklatılmış duruma geçer; Resume() ile tamamlanır, Cancel() ile OperationCanceledException fırlatır.</summary>
    internal Task PauseUntilResumedAsync(string? reason)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _resumeSignal = signal;
        SetState(TransferState.Paused, reason);
        return signal.Task.WaitAsync(_cts.Token);
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

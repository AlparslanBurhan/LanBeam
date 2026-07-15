using LanBeam.App.Localization;
using LanBeam.App.Services;
using LanBeam.Core.Transfer;

namespace LanBeam.App.ViewModels;

public sealed class TransferItemViewModel : ObservableObject
{
    private long _lastBytes;
    private DateTimeOffset _lastTick = DateTimeOffset.Now;
    private double _speedBytesPerSec;

    public TransferHandle Handle { get; }

    public TransferItemViewModel(TransferHandle handle)
    {
        Handle = handle;
        _lastBytes = handle.BytesTransferred;
        CancelCommand = new RelayCommand(_ => Handle.Cancel(), _ => !Handle.IsFinished);
    }

    public RelayCommand CancelCommand { get; }

    public string Title => Handle.DisplayName;
    public string DirectionArrow => Handle.Direction == TransferDirection.Send ? "↑" : "↓";
    public string PeerText => Handle.Direction == TransferDirection.Send
        ? Loc.Format("Str_ReceiverFormat", Handle.PeerName)
        : Loc.Format("Str_SenderFormat", Handle.PeerName);

    public double ProgressPercent => Handle.TotalBytes <= 0
        ? 0 : Math.Min(100.0, Handle.BytesTransferred * 100.0 / Handle.TotalBytes);

    public string BytesText => Handle.TotalBytes > 0
        ? $"{Format.Bytes(Handle.BytesTransferred)} / {Format.Bytes(Handle.TotalBytes)}"
        : Format.Bytes(Handle.BytesTransferred);

    public string SpeedText => Handle.State == TransferState.Transferring
        ? Format.Speed(_speedBytesPerSec) : "";

    public string EtaText => Handle.State == TransferState.Transferring
        ? Loc.Format("Str_RemainingFormat",
            Format.Eta(Handle.TotalBytes - Handle.BytesTransferred, _speedBytesPerSec)) : "";

    public bool IsActive => !Handle.IsFinished;

    public string StateText => Handle.State switch
    {
        TransferState.Connecting => Loc.Get("Str_StateConnecting"),
        TransferState.Pairing => Loc.Get("Str_StatePairing"),
        TransferState.WaitingApproval => Loc.Get("Str_StateWaitingApproval"),
        TransferState.Transferring => Loc.Get("Str_StateTransferring"),
        TransferState.Verifying => Loc.Get("Str_StateVerifying"),
        TransferState.Completed => Loc.Get("Str_StateCompleted"),
        TransferState.Rejected => Loc.Get("Str_StateRejected"),
        TransferState.Cancelled => Loc.Get("Str_StateCancelled"),
        TransferState.Failed => Loc.Format("Str_StateFailedFormat", Handle.FailureReason ?? Loc.Get("Str_Unknown")),
        _ => Handle.State.ToString(),
    };

    /// <summary>500 ms'de bir çağrılır: hız hesabı + tüm bağlı özellikleri tazele.</summary>
    public void Tick()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        long bytes = Handle.BytesTransferred;
        double dt = (now - _lastTick).TotalSeconds;
        if (dt > 0.2)
        {
            // Ani sıçramaları yumuşat (üstel kayan ortalama).
            double instant = (bytes - _lastBytes) / dt;
            _speedBytesPerSec = _speedBytesPerSec <= 0 ? instant : _speedBytesPerSec * 0.6 + instant * 0.4;
            _lastBytes = bytes;
            _lastTick = now;
        }

        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(BytesText));
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(EtaText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsActive));
    }
}

using LanBeam.Core.Protocol;

namespace LanBeam.Core.Transfer;

/// <summary>Gönderen tarafın eşleştirme sırasında UI'dan PIN istemesi için sözleşme.</summary>
public interface ISendInteraction
{
    /// <summary>Karşı cihazın ekranındaki PIN'i kullanıcıdan ister. null = kullanıcı vazgeçti.</summary>
    Task<string?> RequestPinAsync(string peerDeviceName, int attemptsLeft, CancellationToken ct);
}

/// <summary>Alıcı tarafta eşleştirme sırasında ekranda gösterilecek PIN bilgisi.</summary>
public sealed class PairingPrompt
{
    private readonly TaskCompletionSource<bool> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancel = new();

    public required string Pin { get; init; }
    public required string PeerName { get; init; }

    /// <summary>
    /// Bu DeviceId daha önce farklı bir sertifika parmak iziyle eşleşmişti — kimlik değişmiş
    /// olabilir. UI belirgin bir uyarı göstermeli.
    /// </summary>
    public bool FingerprintChanged { get; init; }

    /// <summary>true = eşleştirme başarılı. UI bunu bekleyip pencereyi kapatır.</summary>
    public Task<bool> Completion => _done.Task;

    internal CancellationToken CancellationToken => _cancel.Token;

    /// <summary>Kullanıcı PIN penceresini kapatırsa eşleştirme iptal edilir.</summary>
    public void Cancel() => _cancel.Cancel();

    internal void Complete(bool success) => _done.TrySetResult(success);
}

/// <summary>Alıcı tarafta kullanıcı onayı bekleyen gelen transfer isteği.</summary>
public sealed class IncomingOffer
{
    private readonly TaskCompletionSource<string?> _decision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required TransferOffer Offer { get; init; }
    public required string SenderName { get; init; }
    public required string SenderDeviceId { get; init; }
    public required string SenderFingerprint { get; init; }

    /// <summary>Gönderen beklerken bağlantıyı kestiyse tetiklenir; UI pencereyi kapatmalı.</summary>
    public event Action? Revoked;

    /// <summary>Hedef klasör yolu ile kabul; null = red.</summary>
    internal Task<string?> Decision => _decision.Task;

    public void Accept(string destinationRoot) => _decision.TrySetResult(destinationRoot);

    public void Reject() => _decision.TrySetResult(null);

    internal void MarkRevoked()
    {
        _decision.TrySetCanceled();
        Revoked?.Invoke();
    }
}

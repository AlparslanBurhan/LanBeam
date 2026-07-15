namespace LanBeam.Core.Protocol;

/// <summary>Kontrol kanalı mesaj tipleri.</summary>
public static class MessageTypes
{
    public const string Hello = "hello";
    public const string HelloAck = "hello-ack";

    public const string PairingRequired = "pairing-required";
    public const string PairingChallenge = "pairing-challenge";
    public const string PairingProof = "pairing-proof";
    public const string PairingOk = "pairing-ok";
    public const string PairingFail = "pairing-fail";

    public const string Offer = "offer";
    public const string Accept = "accept";
    public const string Reject = "reject";

    public const string Avatar = "avatar";
    public const string Progress = "progress";
    public const string Resend = "resend";
    public const string Complete = "complete";
    public const string Cancel = "cancel";
    public const string Error = "error";
}

public static class ConnectionPurpose
{
    public const string Control = "control";
    public const string Data = "data";
    public const string Avatar = "avatar";
}

public sealed record HelloMessage(
    int ProtocolVersion,
    string Purpose,
    string DeviceId,
    string DeviceName,
    string? SessionId = null,
    string? DataToken = null);

public sealed record HelloAckMessage(string DeviceId, string DeviceName, bool Trusted);

public sealed record FileEntry(int Id, string RelativePath, long Size);

/// <summary>Gönderilecek içeriğin tam metadata'sı.</summary>
public sealed record TransferOffer(
    string TransferId,
    string DisplayName,
    long TotalBytes,
    int FileCount,
    List<FileEntry> Files,
    List<string> EmptyDirectories);

public sealed record AcceptMessage(
    string SessionId,
    string DataToken,
    int StreamCount,
    /// <summary>Resume: fileId → tamamlanmış chunk indeksleri (gönderen bunları atlar).</summary>
    Dictionary<int, int[]>? CompletedChunks = null);

public sealed record RejectMessage(string Reason);

public sealed record ProgressMessage(long BytesReceived);

/// <summary>Hash uyuşmazlığı olan parçaların yeniden gönderim isteği.</summary>
public sealed record ResendMessage(List<ChunkRef> Chunks);

public sealed record ChunkRef(int FileId, long Offset, int Length);

public sealed record PairingChallengeMessage(string VerifierNonceBase64, int AttemptsLeft);

public sealed record PairingProofMessage(string ProverNonceBase64, string ProofBase64);

public sealed record PairingOkMessage(string ProofBase64);

public sealed record PairingFailMessage(int AttemptsLeft);

public sealed record ErrorMessage(string Message);

/// <summary>Avatar yanıtı: özel fotoğraf varsa PNG bayları, yoksa yalnızca etiket.</summary>
public sealed record AvatarMessage(string Tag, string? PngBase64);

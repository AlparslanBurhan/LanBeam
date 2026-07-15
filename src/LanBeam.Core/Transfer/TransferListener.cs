using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using LanBeam.Core.Protocol;
using LanBeam.Core.Security;

namespace LanBeam.Core.Transfer;

/// <summary>
/// Gelen bağlantıları dinler: kontrol bağlantılarında eşleştirme + teklif/onay akışını yürütür,
/// veri bağlantılarını aktif oturumlara bağlar.
/// </summary>
public sealed class TransferListener : IDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly string _ownFingerprint;
    private readonly TrustedDeviceStore _trust;
    private readonly string _deviceId;
    private readonly Func<string> _deviceName;
    private readonly Func<int> _defaultStreamCount;

    /// <summary>Kendi avatarımız: (etiket, özel fotoğraf PNG'si ya da null). UI/Node tarafından atanır.</summary>
    public Func<(string Tag, byte[]? Png)>? AvatarProvider { get; set; }

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PairingResponseTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PairingCooldown = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, ReceiveSession> _sessions = new();
    private readonly SemaphoreSlim _connectionSlots = new(ProtocolConstants.MaxConcurrentConnections);
    private readonly ConcurrentDictionary<string, bool> _pairingInProgressByIp = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentPairingByIp = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Alıcı tarafta PIN gösterilmeli.</summary>
    public event Action<PairingPrompt>? PairingStarted;

    /// <summary>Kullanıcı onayı bekleyen gelen transfer isteği.</summary>
    public event Action<IncomingOffer>? OfferReceived;

    /// <summary>Kabul edilen transfer başladı (UI listesine eklensin).</summary>
    public event Action<TransferHandle>? TransferStarted;

    public TransferListener(X509Certificate2 certificate, string ownFingerprint,
        TrustedDeviceStore trust, string deviceId, Func<string> deviceName, Func<int> defaultStreamCount)
    {
        _certificate = certificate;
        _ownFingerprint = ownFingerprint;
        _trust = trust;
        _deviceId = deviceId;
        _deviceName = deviceName;
        _defaultStreamCount = defaultStreamCount;
    }

    public void Start(int port)
    {
        if (_listener is not null) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { } listener)
        {
            Socket socket;
            try { socket = await listener.AcceptSocketAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }

            // Eşzamanlı bağlantı üst sınırı: doluysa fazlayı hemen kapat (bağlantı seli koruması).
            if (!_connectionSlots.Wait(0))
            {
                socket.Dispose();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try { await HandleConnectionAsync(socket, ct).ConfigureAwait(false); }
                finally { _connectionSlots.Release(); }
            }, ct);
        }
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken ct)
    {
        SslStream? ssl = null;
        string remoteIp = (socket.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        try
        {
            TlsHelper.TuneSocket(socket);

            // El sıkışma + ilk mesaj için zaman aşımı: TLS'i tamamlayıp veri göndermeyen
            // istemcilerin (slowloris) bağlantı yuvasını süresiz tutmasını engeller.
            HelloMessage hello;
            JsonChannel channel;
            string peerFp;
            using (var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                handshakeCts.CancelAfter(HandshakeTimeout);
                (ssl, peerFp) = await TlsHelper.AuthenticateAsServerAsync(socket, _certificate, handshakeCts.Token)
                    .ConfigureAwait(false);

                channel = new JsonChannel(ssl);
                ReceivedMessage? helloMsg = await channel.ReceiveAsync(handshakeCts.Token).ConfigureAwait(false);
                if (helloMsg is null || helloMsg.Type != MessageTypes.Hello)
                    return;

                hello = helloMsg.As<HelloMessage>();
            }

            if (hello.ProtocolVersion != ProtocolConstants.ProtocolVersion)
            {
                await channel.SendAsync(MessageTypes.Error,
                    new ErrorMessage("Uyumsuz protokol sürümü. İki tarafta da güncel sürümü kullanın."), ct)
                    .ConfigureAwait(false);
                return;
            }

            switch (hello.Purpose)
            {
                case ConnectionPurpose.Control:
                    await HandleControlAsync(channel, hello, peerFp, remoteIp, ct).ConfigureAwait(false);
                    break;
                case ConnectionPurpose.Data:
                    await HandleDataAsync(ssl, channel, hello, peerFp, ct).ConfigureAwait(false);
                    break;
                case ConnectionPurpose.Avatar:
                    // Avatar yalnızca güvenilir (eşleşmiş) cihazlara sunulur — eşleşmemiş bir
                    // cihaz özel fotoğrafımızı çekemez (gizlilik).
                    if (_trust.IsTrusted(peerFp))
                    {
                        (string tag, byte[]? png) = AvatarProvider?.Invoke() ?? ("", null);
                        await channel.SendAsync(MessageTypes.Avatar, new AvatarMessage(
                            tag, png is null ? null : Convert.ToBase64String(png)), ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await channel.SendAsync(MessageTypes.Avatar, new AvatarMessage("", null), ct)
                            .ConfigureAwait(false);
                    }
                    break;
            }
        }
        catch (Exception)
        {
            // Tekil bağlantı hataları dinleyiciyi düşürmez.
        }
        finally
        {
            if (ssl is not null)
            {
                try { await ssl.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            }
            else
            {
                socket.Dispose();
            }
        }
    }

    private async Task HandleControlAsync(JsonChannel channel, HelloMessage hello, string peerFp,
        string remoteIp, CancellationToken ct)
    {
        bool trusted = _trust.IsTrusted(peerFp);
        await channel.SendAsync(MessageTypes.HelloAck,
            new HelloAckMessage(_deviceId, _deviceName(), trusted), ct).ConfigureAwait(false);

        if (!trusted && !await RunReceiverPairingAsync(channel, hello, peerFp, remoteIp, ct).ConfigureAwait(false))
            return;

        // Buraya ulaşıldıysa karşı taraf güvenilir (ya baştan ya da eşleşme sonrası).
        // Yalnızca şimdi büyük Offer çerçevesine izin ver — eşleşmemiş cihaz asla büyük
        // tampon ayırtamaz.
        channel.MaxFrameBytes = ProtocolConstants.MaxOfferFrame;

        while (true)
        {
            ReceivedMessage? msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
            if (msg is null) return;

            switch (msg.Type)
            {
                case MessageTypes.PairingRequired:
                    if (!await RunReceiverPairingAsync(channel, hello, peerFp, remoteIp, ct).ConfigureAwait(false))
                        return;
                    break;

                case MessageTypes.Offer:
                    await HandleOfferAsync(channel, hello, peerFp, msg.As<TransferOffer>(), ct)
                        .ConfigureAwait(false);
                    return; // teklif akışı bitince kontrol bağlantısı kapanır

                case MessageTypes.Cancel:
                    return;

                default:
                    return;
            }
        }
    }

    private async Task<bool> RunReceiverPairingAsync(JsonChannel channel, HelloMessage hello,
        string peerFp, string remoteIp, CancellationToken ct)
    {
        // PIN penceresi seli koruması: aynı IP için aynı anda tek bekleyen pencere + kısa cooldown.
        // Eşleşmemiş bir cihazın ekranı PIN pencereleriyle boğmasını engeller.
        if (!_pairingInProgressByIp.TryAdd(remoteIp, true))
            return false;

        try
        {
            if (_recentPairingByIp.TryGetValue(remoteIp, out DateTimeOffset last) &&
                DateTimeOffset.UtcNow - last < PairingCooldown)
            {
                return false;
            }

            // Bilinen bir cihazın DeviceId'si farklı bir parmak iziyle geliyorsa kullanıcıyı uyar
            // (kimlik değişmiş olabilir — sabitleme sinyali).
            TrustedDevice? knownById = _trust.FindByDeviceId(hello.DeviceId);
            bool fingerprintChanged = knownById is not null &&
                !string.Equals(knownById.CertFingerprint, peerFp, StringComparison.OrdinalIgnoreCase);

            return await RunPairingLoopAsync(channel, hello, peerFp, fingerprintChanged, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _recentPairingByIp[remoteIp] = DateTimeOffset.UtcNow;
            _pairingInProgressByIp.TryRemove(remoteIp, out _);
        }
    }

    private async Task<bool> RunPairingLoopAsync(JsonChannel channel, HelloMessage hello,
        string peerFp, bool fingerprintChanged, CancellationToken ct)
    {
        string pin = PairingMath.GeneratePin();
        var prompt = new PairingPrompt
        {
            Pin = pin,
            PeerName = hello.DeviceName,
            FingerprintChanged = fingerprintChanged,
        };
        PairingStarted?.Invoke(prompt);

        // Karşı taraf makul sürede PIN kanıtı göndermezse bağlantı yuvasını serbest bırak.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, prompt.CancellationToken);
        linked.CancelAfter(PairingResponseTimeout);

        try
        {
            for (int attemptsLeft = PairingMath.MaxAttempts; attemptsLeft >= 1; attemptsLeft--)
            {
                byte[] verifierNonce = PairingMath.GenerateNonce();
                await channel.SendAsync(MessageTypes.PairingChallenge, new PairingChallengeMessage(
                    Convert.ToBase64String(verifierNonce), attemptsLeft), linked.Token).ConfigureAwait(false);

                ReceivedMessage? msg = await channel.ReceiveAsync(linked.Token).ConfigureAwait(false);
                if (msg is null || msg.Type != MessageTypes.PairingProof)
                {
                    prompt.Complete(false);
                    return false;
                }

                PairingProofMessage proofMsg = msg.As<PairingProofMessage>();
                byte[] proverNonce = Convert.FromBase64String(proofMsg.ProverNonceBase64);
                byte[] proof = Convert.FromBase64String(proofMsg.ProofBase64);
                byte[] expected = PairingMath.ComputeProof(pin, peerFp, _ownFingerprint,
                    proverNonce, verifierNonce);

                if (PairingMath.VerifyProof(expected, proof))
                {
                    byte[] ourProof = PairingMath.ComputeProof(pin, _ownFingerprint, peerFp,
                        verifierNonce, proverNonce);
                    await channel.SendAsync(MessageTypes.PairingOk,
                        new PairingOkMessage(Convert.ToBase64String(ourProof)), linked.Token)
                        .ConfigureAwait(false);

                    _trust.AddOrUpdate(hello.DeviceId, hello.DeviceName, peerFp);
                    prompt.Complete(true);
                    return true;
                }

                await channel.SendAsync(MessageTypes.PairingFail,
                    new PairingFailMessage(attemptsLeft - 1), linked.Token).ConfigureAwait(false);
            }

            prompt.Complete(false);
            return false;
        }
        catch (Exception)
        {
            prompt.Complete(false);
            throw;
        }
    }

    private async Task HandleOfferAsync(JsonChannel channel, HelloMessage hello, string peerFp,
        TransferOffer offer, CancellationToken ct)
    {
        var incoming = new IncomingOffer
        {
            Offer = offer,
            SenderName = hello.DeviceName,
            SenderDeviceId = hello.DeviceId,
            SenderFingerprint = peerFp,
        };
        OfferReceived?.Invoke(incoming);

        // Kullanıcı karar verirken gönderenin vazgeçmesini de dinle.
        Task<ReceivedMessage?> readTask = channel.ReceiveAsync(ct);
        Task<string?> decisionTask = incoming.Decision;

        Task first = await Task.WhenAny(decisionTask, readTask).ConfigureAwait(false);
        if (first == readTask)
        {
            // EOF ya da cancel: gönderen vazgeçti.
            incoming.MarkRevoked();
            return;
        }

        string? destination = await decisionTask.ConfigureAwait(false);
        if (destination is null)
        {
            await channel.SendAsync(MessageTypes.Reject,
                new RejectMessage("Alıcı transferi reddetti."), ct).ConfigureAwait(false);
            return;
        }

        // Kötü niyetli/hatalı teklif doğrulaması: yinelenen fileId, negatif boyut, disk boş alanı.
        string? validationError = ValidateOffer(offer, destination);
        if (validationError is not null)
        {
            await channel.SendAsync(MessageTypes.Reject, new RejectMessage(validationError), ct)
                .ConfigureAwait(false);
            return;
        }

        var handle = new TransferHandle
        {
            Direction = TransferDirection.Receive,
            DisplayName = offer.DisplayName,
            PeerName = hello.DeviceName,
        };

        int streamCount = Math.Clamp(_defaultStreamCount(), 1, 8);
        using var session = new ReceiveSession(offer, destination, peerFp, streamCount, handle);
        if (!_sessions.TryAdd(session.SessionId, session))
            throw new InvalidOperationException("Oturum kimliği çakışması.");

        try
        {
            session.ResendNeeded += chunks =>
            {
                _ = channel.SendAsync(MessageTypes.Resend, new ResendMessage(chunks), ct);
            };

            handle.SetState(TransferState.Transferring);
            TransferStarted?.Invoke(handle);

            await channel.SendAsync(MessageTypes.Accept, new AcceptMessage(
                session.SessionId, session.DataToken, streamCount,
                session.GetResumeCompletedChunks() is { Count: > 0 } resume ? resume : null), ct)
                .ConfigureAwait(false);

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.CancellationToken);

            // İlerleme raporu + periyodik resume kaydı
            Task progressLoop = Task.Run(async () =>
            {
                int tick = 0;
                while (!session.Completion.IsCompleted && !sessionCts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(500, sessionCts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }

                    try
                    {
                        await channel.SendAsync(MessageTypes.Progress,
                            new ProgressMessage(handle.BytesTransferred), sessionCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception) { break; }

                    handle.NotifyProgress();
                    if (++tick % 6 == 0)
                        session.SavePartial();
                }
            });

            // Gönderenden gelen mesajları dinle (iptal/hata) — okuyucu tek olmalı,
            // bekleyen readTask'ten devam et.
            Task controlLoop = Task.Run(async () =>
            {
                Task<ReceivedMessage?> pending = readTask;
                while (!session.Completion.IsCompleted)
                {
                    ReceivedMessage? msg;
                    try { msg = await pending.ConfigureAwait(false); }
                    catch (Exception) { session.Fail("Bağlantı koptu."); break; }

                    if (msg is null) { session.Fail("Bağlantı koptu."); break; }
                    if (msg.Type == MessageTypes.Cancel) { session.Fail("Gönderen transferi iptal etti."); break; }
                    if (msg.Type == MessageTypes.Error) { session.Fail(msg.As<ErrorMessage>().Message); break; }

                    pending = channel.ReceiveAsync(sessionCts.Token);
                }
            });

            Task<string?> completionTask = session.Completion;
            Task cancelledTask = Task.Delay(Timeout.Infinite, sessionCts.Token);
            Task finished = await Task.WhenAny(completionTask, cancelledTask).ConfigureAwait(false);

            if (finished == cancelledTask && handle.CancellationToken.IsCancellationRequested)
            {
                session.SavePartial();
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await channel.SendAsync(MessageTypes.Cancel, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
                handle.SetState(TransferState.Cancelled, "Transfer iptal edildi.");
                return;
            }

            string? failure = await completionTask.ConfigureAwait(false);
            if (failure is null)
            {
                await channel.SendAsync(MessageTypes.Progress,
                    new ProgressMessage(handle.BytesTransferred), ct).ConfigureAwait(false);
                await channel.SendAsync(MessageTypes.Complete, ct).ConfigureAwait(false);
                handle.SetState(TransferState.Completed);
            }
            else
            {
                handle.SetState(TransferState.Failed, failure);
            }
        }
        finally
        {
            _sessions.TryRemove(session.SessionId, out _);
        }
    }

    /// <summary>Teklifi kabul etmeden önce doğrular. Sorun varsa reddetme nedenini, yoksa null döner.</summary>
    private static string? ValidateOffer(TransferOffer offer, string destination)
    {
        if (offer.Files.Count == 0 && offer.EmptyDirectories.Count == 0)
            return "Boş teklif.";

        if (offer.Files.Any(f => f.Size < 0))
            return "Geçersiz dosya boyutu (negatif).";

        // Yinelenen fileId → alıcıda sessiz veri kaybına yol açar; reddet.
        if (offer.Files.Select(f => f.Id).Distinct().Count() != offer.Files.Count)
            return "Teklifte yinelenen dosya kimliği var.";

        // Hedef diskte yeterli boş alan var mı? (Devasa boyut bildirip diski doldurmayı engeller.)
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (!string.IsNullOrEmpty(root))
            {
                long free = new DriveInfo(root).AvailableFreeSpace;
                // %2 pay bırak.
                if (offer.TotalBytes > free - free / 50)
                    return $"Hedef diskte yeterli alan yok ({offer.TotalBytes / (1024 * 1024)} MB gerekli).";
            }
        }
        catch (Exception)
        {
            // Boş alan belirlenemedi (ör. ağ yolu): engelleme, transfere izin ver.
        }

        return null;
    }

    private async Task HandleDataAsync(SslStream ssl, JsonChannel channel, HelloMessage hello,
        string peerFp, CancellationToken ct)
    {
        if (hello.SessionId is null ||
            !_sessions.TryGetValue(hello.SessionId, out ReceiveSession? session) ||
            !string.Equals(session.DataToken, hello.DataToken, StringComparison.Ordinal) ||
            !string.Equals(session.SenderFingerprint, peerFp, StringComparison.OrdinalIgnoreCase))
        {
            return; // geçersiz veri bağlantısı: sessizce kapat
        }

        await channel.SendAsync(MessageTypes.HelloAck,
            new HelloAckMessage(_deviceId, _deviceName(), true), ct).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Handle.CancellationToken);
        try
        {
            await session.RunDataStreamAsync(ssl, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!session.Completion.IsCompleted)
                session.Fail($"Veri akışı hatası: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
        _connectionSlots.Dispose();
        _cts = null;
        _listener = null;
    }
}

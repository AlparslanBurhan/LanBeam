using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using LanBeam.Core.Models;
using LanBeam.Core.Protocol;
using LanBeam.Core.Security;
using Microsoft.Win32.SafeHandles;

namespace LanBeam.Core.Transfer;

/// <summary>Gönderen tarafın tüm akışı: bağlan → (gerekirse eşleş) → teklif → paralel veri gönderimi.</summary>
public sealed class TransferSender
{
    private readonly X509Certificate2 _certificate;
    private readonly string _ownFingerprint;
    private readonly TrustedDeviceStore _trust;
    private readonly string _deviceId;
    private readonly Func<string> _deviceName;

    public TransferSender(X509Certificate2 certificate, string ownFingerprint,
        TrustedDeviceStore trust, string deviceId, Func<string> deviceName)
    {
        _certificate = certificate;
        _ownFingerprint = ownFingerprint;
        _trust = trust;
        _deviceId = deviceId;
        _deviceName = deviceName;
    }

    public async Task RunAsync(DeviceInfo target, ScannedTree tree, ISendInteraction interaction,
        TransferHandle handle)
    {
        CancellationToken ct = handle.CancellationToken;
        var dataStreams = new List<SslStream>();
        JsonChannel? channel = null;

        try
        {
            handle.SetState(TransferState.Connecting);

            (SslStream control, string peerFp) = await ConnectAsync(target.Address, target.Port, ct)
                .ConfigureAwait(false);
            channel = new JsonChannel(control);

            await channel.SendAsync(MessageTypes.Hello, new HelloMessage(
                ProtocolConstants.ProtocolVersion, ConnectionPurpose.Control, _deviceId, _deviceName()), ct)
                .ConfigureAwait(false);

            ReceivedMessage ack = await ReceiveOrThrowAsync(channel, ct).ConfigureAwait(false);
            if (ack.Type != MessageTypes.HelloAck)
                throw new InvalidDataException($"Beklenmeyen yanıt: {ack.Type}");
            HelloAckMessage ackBody = ack.As<HelloAckMessage>();

            // Sabitlenmiş kimlik kontrolü: bu cihazla daha önce eşleştiysek parmak izi değişemez.
            TrustedDevice? known = _trust.FindByDeviceId(ackBody.DeviceId);
            if (known is not null &&
                !string.Equals(known.CertFingerprint, peerFp, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"'{ackBody.DeviceName}' cihazının kimliği değişmiş görünüyor. Güvenlik nedeniyle " +
                    "transfer engellendi. Cihaz yeniden kurulduysa güvenilir listeden silip tekrar eşleştirin.");
            }

            bool theyTrustUs = ackBody.Trusted;
            bool weTrustThem = _trust.IsTrusted(peerFp);

            if (!theyTrustUs)
            {
                // Alıcı challenge gönderecek.
                handle.SetState(TransferState.Pairing);
                await RunSenderPairingAsync(channel, peerFp, ackBody, interaction, handle, ct)
                    .ConfigureAwait(false);
            }
            else if (!weTrustThem)
            {
                handle.SetState(TransferState.Pairing);
                await channel.SendAsync(MessageTypes.PairingRequired, ct).ConfigureAwait(false);
                await RunSenderPairingAsync(channel, peerFp, ackBody, interaction, handle, ct)
                    .ConfigureAwait(false);
            }

            // Teklif
            handle.SetState(TransferState.WaitingApproval);
            var offer = new TransferOffer(
                TransferId: handle.Id,
                DisplayName: tree.DisplayName,
                TotalBytes: tree.TotalBytes,
                FileCount: tree.Files.Count,
                Files: tree.Files,
                EmptyDirectories: tree.EmptyDirectories);
            await channel.SendAsync(MessageTypes.Offer, offer, ct).ConfigureAwait(false);

            ReceivedMessage decision = await ReceiveOrThrowAsync(channel, ct).ConfigureAwait(false);
            if (decision.Type == MessageTypes.Reject)
            {
                handle.SetState(TransferState.Rejected, decision.As<RejectMessage>().Reason);
                return;
            }
            if (decision.Type != MessageTypes.Accept)
                throw new InvalidDataException($"Beklenmeyen yanıt: {decision.Type}");

            AcceptMessage accept = decision.As<AcceptMessage>();
            int streamCount = Math.Clamp(accept.StreamCount, 1, 8);

            // Resume: alıcının elindeki parçaları atla.
            Dictionary<int, HashSet<int>>? completed = accept.CompletedChunks?
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToHashSet());
            List<ChunkWork> chunks = ChunkPlanner.Plan(tree.Files, completed);

            long skippedBytes = tree.TotalBytes - chunks.Sum(c => (long)c.Length);
            handle.SetBytes(skippedBytes);
            handle.SetState(TransferState.Transferring);

            var queue = new ConcurrentQueue<ChunkWork>(chunks);
            var resendChannel = Channel.CreateUnbounded<List<ChunkRef>>();

            // Paralel veri akışları
            var workers = new List<Task>();
            for (int i = 0; i < streamCount; i++)
            {
                (SslStream dataStream, string dataPeerFp) = await ConnectAsync(target.Address, target.Port, ct)
                    .ConfigureAwait(false);
                if (!string.Equals(dataPeerFp, peerFp, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Veri bağlantısında sertifika değişti.");
                dataStreams.Add(dataStream);

                var dataChannel = new JsonChannel(dataStream);
                await dataChannel.SendAsync(MessageTypes.Hello, new HelloMessage(
                    ProtocolConstants.ProtocolVersion, ConnectionPurpose.Data, _deviceId, _deviceName(),
                    accept.SessionId, accept.DataToken), ct).ConfigureAwait(false);
                ReceivedMessage dataAck = await ReceiveOrThrowAsync(dataChannel, ct).ConfigureAwait(false);
                if (dataAck.Type != MessageTypes.HelloAck)
                    throw new InvalidDataException("Veri bağlantısı reddedildi.");

                bool handlesResend = i == 0; // yeniden gönderim dalgalarını ilk akış üstlenir
                workers.Add(Task.Run(() => RunDataWorkerAsync(dataStream, tree, queue,
                    handlesResend ? resendChannel.Reader : null, ct), ct));
            }

            // Kontrol döngüsü: ilerleme/yeniden gönderim/sonuç mesajları
            while (true)
            {
                ReceivedMessage? msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
                if (msg is null)
                    throw new IOException("Bağlantı beklenmedik şekilde kapandı.");

                switch (msg.Type)
                {
                    case MessageTypes.Progress:
                        handle.SetBytes(msg.As<ProgressMessage>().BytesReceived);
                        handle.NotifyProgress();
                        break;

                    case MessageTypes.Resend:
                        await resendChannel.Writer.WriteAsync(msg.As<ResendMessage>().Chunks, ct)
                            .ConfigureAwait(false);
                        break;

                    case MessageTypes.Complete:
                        resendChannel.Writer.TryComplete();
                        await Task.WhenAll(workers).ConfigureAwait(false);
                        handle.SetBytes(tree.TotalBytes);
                        handle.SetState(TransferState.Completed);
                        return;

                    case MessageTypes.Cancel:
                        handle.SetState(TransferState.Cancelled, "Alıcı transferi iptal etti.");
                        return;

                    case MessageTypes.Error:
                        throw new IOException(msg.As<ErrorMessage>().Message);
                }
            }
        }
        catch (OperationCanceledException) when (handle.CancellationToken.IsCancellationRequested)
        {
            if (channel is not null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await channel.SendAsync(MessageTypes.Cancel, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
            }
            handle.SetState(TransferState.Cancelled, "Transfer iptal edildi.");
        }
        catch (Exception ex)
        {
            handle.SetState(TransferState.Failed, ex.Message);
        }
        finally
        {
            foreach (SslStream s in dataStreams)
            {
                try { await s.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            }
            if (channel is not null)
            {
                try { await channel.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            }
        }
    }

    /// <summary>Karşı cihazın özel avatar fotoğrafını çeker (yoksa/preset ise null).</summary>
    public async Task<byte[]?> FetchAvatarAsync(DeviceInfo target, CancellationToken ct)
    {
        (SslStream stream, _) = await ConnectAsync(target.Address, target.Port, ct).ConfigureAwait(false);
        await using var channel = new JsonChannel(stream);

        await channel.SendAsync(MessageTypes.Hello, new HelloMessage(
            ProtocolConstants.ProtocolVersion, ConnectionPurpose.Avatar, _deviceId, _deviceName()), ct)
            .ConfigureAwait(false);

        ReceivedMessage? msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        if (msg is null || msg.Type != MessageTypes.Avatar)
            return null;

        AvatarMessage avatar = msg.As<AvatarMessage>();
        if (avatar.PngBase64 is null)
            return null;

        byte[] png = Convert.FromBase64String(avatar.PngBase64);
        return png.Length <= AvatarTags.MaxImageBytes ? png : null;
    }

    private async Task<(SslStream, string)> ConnectAsync(string address, int port, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            TlsHelper.TuneSocket(socket);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(address), port), ct).ConfigureAwait(false);
            return await TlsHelper.AuthenticateAsClientAsync(socket, _certificate, ct).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task RunSenderPairingAsync(JsonChannel channel, string peerFp, HelloAckMessage peer,
        ISendInteraction interaction, TransferHandle handle, CancellationToken ct)
    {
        string? pin = null;
        byte[]? lastSenderNonce = null;
        byte[]? lastReceiverNonce = null;

        while (true)
        {
            ReceivedMessage msg = await ReceiveOrThrowAsync(channel, ct).ConfigureAwait(false);

            switch (msg.Type)
            {
                case MessageTypes.PairingChallenge:
                {
                    PairingChallengeMessage challenge = msg.As<PairingChallengeMessage>();
                    pin = await interaction.RequestPinAsync(peer.DeviceName, challenge.AttemptsLeft, ct)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(pin))
                        throw new OperationCanceledException("Eşleştirme kullanıcı tarafından iptal edildi.");

                    lastReceiverNonce = Convert.FromBase64String(challenge.VerifierNonceBase64);
                    lastSenderNonce = PairingMath.GenerateNonce();
                    byte[] proof = PairingMath.ComputeProof(pin, _ownFingerprint, peerFp,
                        lastSenderNonce, lastReceiverNonce);

                    await channel.SendAsync(MessageTypes.PairingProof, new PairingProofMessage(
                        Convert.ToBase64String(lastSenderNonce), Convert.ToBase64String(proof)), ct)
                        .ConfigureAwait(false);
                    break;
                }

                case MessageTypes.PairingOk:
                {
                    if (pin is null || lastSenderNonce is null || lastReceiverNonce is null)
                        throw new InvalidDataException("Beklenmeyen eşleştirme onayı.");

                    byte[] theirProof = Convert.FromBase64String(msg.As<PairingOkMessage>().ProofBase64);
                    byte[] expected = PairingMath.ComputeProof(pin, peerFp, _ownFingerprint,
                        lastReceiverNonce, lastSenderNonce);
                    if (!PairingMath.VerifyProof(expected, theirProof))
                        throw new InvalidOperationException(
                            "Karşı taraf doğrulanamadı (araya girme girişimi olabilir). Transfer durduruldu.");

                    _trust.AddOrUpdate(peer.DeviceId, peer.DeviceName, peerFp);
                    return;
                }

                case MessageTypes.PairingFail:
                {
                    PairingFailMessage fail = msg.As<PairingFailMessage>();
                    if (fail.AttemptsLeft <= 0)
                        throw new InvalidOperationException("PIN 3 kez yanlış girildi. Eşleştirme reddedildi.");
                    break; // yeni challenge gelecek
                }

                default:
                    throw new InvalidDataException($"Eşleştirme sırasında beklenmeyen mesaj: {msg.Type}");
            }
        }
    }

    private static async Task RunDataWorkerAsync(SslStream stream, ScannedTree tree,
        ConcurrentQueue<ChunkWork> queue, ChannelReader<List<ChunkRef>>? resendReader, CancellationToken ct)
    {
        byte[] buffer = new byte[ProtocolConstants.ChunkSize];
        SafeFileHandle? currentHandle = null;
        int currentFileId = -1;

        try
        {
            while (queue.TryDequeue(out ChunkWork chunk))
                await SendOneAsync(chunk.FileId, chunk.Offset, chunk.Length).ConfigureAwait(false);

            await DataFraming.SendEndOfStreamAsync(stream, ct).ConfigureAwait(false);

            // Yeniden gönderim dalgaları (yalnızca ilk akış üstlenir)
            if (resendReader is not null)
            {
                while (await resendReader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (resendReader.TryRead(out List<ChunkRef>? refs))
                    {
                        foreach (ChunkRef r in refs)
                            await SendOneAsync(r.FileId, r.Offset, r.Length).ConfigureAwait(false);
                    }
                    await DataFraming.SendEndOfStreamAsync(stream, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            currentHandle?.Dispose();
        }

        async Task SendOneAsync(int fileId, long offset, int length)
        {
            if (fileId != currentFileId)
            {
                currentHandle?.Dispose();
                currentHandle = File.OpenHandle(tree.LocalPathsByFileId[fileId], FileMode.Open,
                    FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
                currentFileId = fileId;
            }

            int total = 0;
            while (total < length)
            {
                int read = RandomAccess.Read(currentHandle!, buffer.AsSpan(total, length - total), offset + total);
                if (read == 0)
                    throw new IOException($"Dosya beklenenden kısa (transfer sırasında değişmiş olabilir): " +
                        $"{tree.LocalPathsByFileId[fileId]}");
                total += read;
            }

            await DataFraming.SendChunkAsync(stream, fileId, offset, buffer.AsMemory(0, length), ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task<ReceivedMessage> ReceiveOrThrowAsync(JsonChannel channel, CancellationToken ct)
    {
        ReceivedMessage? msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        return msg ?? throw new IOException("Bağlantı karşı tarafça kapatıldı.");
    }
}

using System.Buffers.Binary;

namespace Mixr.Services;

public enum FirmwareUpdateOutcome
{
    Success,
    Unsupported,
    Failed,
    Cancelled,
}

public sealed record FirmwareUpdateResult(FirmwareUpdateOutcome Outcome, string Message);

/// <summary>Fortschritt in Prozent (0–100) plus Statuszeile für die UI.</summary>
public sealed record FirmwareUpdateProgress(int Percent, string Stage);

/// <summary>
/// Firmware-Update über die bestehende Protokollverbindung (FW_BEGIN → FW_CHUNK… → FW_END).
/// Stop-and-wait pro Chunk mit ACK; bei OUT_OF_SEQUENCE synchronisiert der Host auf den vom Gerät gemeldeten Offset.
/// </summary>
public sealed class FirmwareUpdateService
{
    readonly IMixrLink _link;
    readonly EspIncomingDispatcher _dispatcher;
    readonly Action<string> _log;

    static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(3);
    const int MaxRetriesPerChunk = 5;

    public FirmwareUpdateService(IMixrLink link, EspIncomingDispatcher dispatcher, Action<string> log)
    {
        _link = link;
        _dispatcher = dispatcher;
        _log = log;
    }

    public async Task<FirmwareUpdateResult> UpdateAsync(
        FirmwareImage image,
        IProgress<FirmwareUpdateProgress>? progress,
        CancellationToken ct)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<(MixrProtocol.FwStatus, uint)>();

        void OnAck(MixrProtocol.FwStatus s, uint o) => channel.Writer.TryWrite((s, o));
        _dispatcher.FirmwareAck += OnAck;

        try
        {
            progress?.Report(new FirmwareUpdateProgress(0, "Update wird gestartet …"));

            var begin = new byte[4 + 32];
            BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(0, 4), (uint)image.Bytes.Length);
            image.Sha256.CopyTo(begin, 4);
            _link.Send(MixrProtocol.TypeFwBegin, begin);

            var (st, _) = await WaitAck(channel, ct).ConfigureAwait(false);
            if (st == MixrProtocol.FwStatus.Unsupported)
                return new FirmwareUpdateResult(FirmwareUpdateOutcome.Unsupported, MixrProtocol.Describe(st));
            if (st != MixrProtocol.FwStatus.Ok)
                return Fail(st);

            uint offset = 0;
            var total = (uint)image.Bytes.Length;
            var chunk = new byte[4 + MixrProtocol.FwChunkDataMax];
            int retries = 0;
            int lastPct = -1;

            while (offset < total)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min((uint)MixrProtocol.FwChunkDataMax, total - offset);
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(0, 4), offset);
                image.Bytes.AsSpan((int)offset, n).CopyTo(chunk.AsSpan(4));
                _link.Send(MixrProtocol.TypeFwChunk, chunk.AsSpan(0, 4 + n));

                var (status, next) = await WaitAck(channel, ct).ConfigureAwait(false);
                switch (status)
                {
                    case MixrProtocol.FwStatus.Ok:
                        offset = next;
                        retries = 0;
                        break;
                    case MixrProtocol.FwStatus.OutOfSequence:
                        if (++retries > MaxRetriesPerChunk)
                            return Fail(status);
                        _log($"FW: Resync auf Offset {next} (war {offset})");
                        offset = next;
                        break;
                    default:
                        return Fail(status);
                }

                int pct = (int)((long)offset * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    progress?.Report(new FirmwareUpdateProgress(pct, $"Übertrage Firmware … {offset:N0} / {total:N0} Byte"));
                }
            }

            progress?.Report(new FirmwareUpdateProgress(100, "Image wird geprüft, Gerät startet neu …"));
            _link.Send(MixrProtocol.TypeFwEnd);
            var (endStatus, _) = await WaitAck(channel, ct).ConfigureAwait(false);
            if (endStatus != MixrProtocol.FwStatus.Ok)
                return Fail(endStatus);

            _log($"FW: Update auf {image.Version} übertragen ({total:N0} Byte), Gerät startet neu.");
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Success, $"Firmware {image.Version} installiert. Das Gerät startet neu.");
        }
        catch (OperationCanceledException)
        {
            TryAbort();
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Cancelled, "Abgebrochen.");
        }
        catch (TimeoutException)
        {
            TryAbort();
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, "Keine Antwort vom Gerät (Timeout).");
        }
        catch (Exception ex)
        {
            TryAbort();
            return new FirmwareUpdateResult(FirmwareUpdateOutcome.Failed, ex.Message);
        }
        finally
        {
            _dispatcher.FirmwareAck -= OnAck;
        }
    }

    void TryAbort() => _link.TrySend(MixrProtocol.TypeFwAbort);

    static FirmwareUpdateResult Fail(MixrProtocol.FwStatus s) =>
        new(FirmwareUpdateOutcome.Failed, MixrProtocol.Describe(s));

    static async Task<(MixrProtocol.FwStatus, uint)> WaitAck(
        System.Threading.Channels.Channel<(MixrProtocol.FwStatus, uint)> channel, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(AckTimeout);
        try
        {
            return await channel.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }
}

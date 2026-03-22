using System.Security.Cryptography;

namespace Mixr.Services;

/// <summary>
/// Verhindert doppelte serielle „Sessions“, wenn SMTC mehrfach feuert ohne neue Metadaten/Cover.
/// </summary>
public sealed class SessionDedup
{
    readonly object _lock = new();
    string _lastTitle = "";
    string _lastArtist = "";
    byte[]? _lastCoverHash;

    /// <returns><c>true</c>, wenn Titel, Artist oder Cover-Inhalt sich vom letzten erfolgreichen Senden unterscheiden.</returns>
    public bool ShouldSend(string? title, string? artist, ReadOnlySpan<byte> coverRgb565)
    {
        var t = title ?? "";
        var a = artist ?? "";
        var hash = SHA256.HashData(coverRgb565);

        lock (_lock)
        {
            if (_lastCoverHash is not null
                && t == _lastTitle
                && a == _lastArtist
                && hash.AsSpan().SequenceEqual(_lastCoverHash))
            {
                return false;
            }

            _lastTitle = t;
            _lastArtist = a;
            _lastCoverHash = hash;
            return true;
        }
    }
}

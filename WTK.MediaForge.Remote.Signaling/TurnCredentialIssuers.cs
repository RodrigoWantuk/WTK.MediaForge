using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Remote;

namespace WTK.MediaForge.Remote.Signaling;

public sealed class NoTurnCredentialIssuer : ITurnCredentialIssuer
{
    public static NoTurnCredentialIssuer Instance { get; } = new();

    private NoTurnCredentialIssuer()
    {
    }

    public ValueTask<IReadOnlyList<WebRtcIceServer>> IssueAsync(
        string subject,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<WebRtcIceServer>>(Array.Empty<WebRtcIceServer>());
    }
}

public sealed class TurnRestCredentialIssuer : ITurnCredentialIssuer
{
    private readonly IReadOnlyList<string> _urls;
    private readonly byte[] _sharedSecret;
    private readonly TimeProvider _timeProvider;

    public TurnRestCredentialIssuer(
        IEnumerable<Uri> urls,
        string sharedSecret,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(urls);
        _urls = urls.Select(ValidateUrl).ToArray();
        if (_urls.Count == 0)
            throw new ArgumentException("At least one TURN URL is required.", nameof(urls));
        if (string.IsNullOrWhiteSpace(sharedSecret) || sharedSecret.Length < 32)
            throw new ArgumentException("TURN REST shared secret must contain at least 32 characters.", nameof(sharedSecret));

        _sharedSecret = Encoding.UTF8.GetBytes(sharedSecret);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<IReadOnlyList<WebRtcIceServer>> IssueAsync(
        string subject,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("TURN credential subject is required.", nameof(subject));
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "TURN credential lifetime must be positive and no greater than 24 hours.");

        var expiry = _timeProvider.GetUtcNow().Add(lifetime).ToUnixTimeSeconds();
        var username = $"{expiry}:{subject}";
        using var hmac = new HMACSHA1(_sharedSecret);
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
        IReadOnlyList<WebRtcIceServer> result =
        [
            new WebRtcIceServer(_urls, username, credential)
        ];
        return ValueTask.FromResult(result);
    }

    private static string ValidateUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || url.Scheme is not ("turn" or "turns"))
            throw new ArgumentException("TURN URLs must use the turn or turns scheme.", nameof(url));
        return url.AbsoluteUri;
    }
}

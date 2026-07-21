using System.Security.Cryptography;

namespace WTK.MediaForge.Remote.Signaling;

internal static class RemoteSceneSecret
{
    public static string Create(int byteCount) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    public static byte[] Hash(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("A non-empty secret is required.", nameof(secret));

        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
    }

    public static bool FixedTimeEquals(string expected, string supplied)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied))
            return false;

        var expectedHash = Hash(expected);
        var suppliedHash = Hash(supplied);
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

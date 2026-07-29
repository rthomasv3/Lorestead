using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Lorestead.Server.Services;

public static class OAuthCrypto
{
    public static string NewToken()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
    }

    public static string HashHex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    // RFC 7636 S256: the challenge is base64url(SHA-256(ascii(verifier))).
    public static bool VerifierMatches(string codeVerifier, string codeChallenge)
    {
        byte[] computed = Encoding.ASCII.GetBytes(Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier))));
        byte[] expected = Encoding.ASCII.GetBytes(codeChallenge);
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    public static string ClientFingerprint(string clientId, string clientSecret)
    {
        return HashHex(clientId + "\n" + clientSecret);
    }
}

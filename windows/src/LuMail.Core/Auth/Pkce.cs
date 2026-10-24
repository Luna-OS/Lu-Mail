using System.Security.Cryptography;
using System.Text;

namespace LuMail.Core.Auth;

/// <summary>Base64url ohne Padding (RFC 4648 §5).</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Ungültige Base64url-Länge.");
        }
        return Convert.FromBase64String(s);
    }

    /// <summary>Zufallswert aus <paramref name="byteCount"/> kryptografisch sicheren Bytes.</summary>
    public static string Random(int byteCount) => Encode(RandomNumberGenerator.GetBytes(byteCount));
}

/// <summary>PKCE-Paar (RFC 7636, Methode S256).</summary>
public sealed record PkcePair(string Verifier, string Challenge)
{
    public const string Method = "S256";
}

public static class Pkce
{
    /// <summary>Erzeugt einen 64-Zeichen-Verifier (48 Zufallsbytes, base64url) und die S256-Challenge.</summary>
    public static PkcePair Create()
    {
        var verifier = Base64Url.Random(48); // 48 Byte → 64 Zeichen
        return new PkcePair(verifier, ComputeChallenge(verifier));
    }

    /// <summary>challenge = BASE64URL(SHA256(ASCII(verifier))).</summary>
    public static string ComputeChallenge(string verifier) =>
        Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Zufälliger OAuth-/Login-state: 32 Byte, base64url.</summary>
    public static string CreateState() => Base64Url.Random(32);
}

using System.Security.Cryptography;
using System.Text;
namespace Playback.Domain.Workspace;
public static class DemoCredentials
{
    public static bool ValidUsername(string name) => name.Length is >= 1 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    public static bool Equal(string supplied, string expected) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    public static string Version(string account, string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\0" + password)));
}

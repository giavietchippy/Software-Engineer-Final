using System.Security.Cryptography;
using System.Text;

namespace SaigonBite.Web.Data;

/// <summary>BR18: passwords are stored hashed, never in plain text.</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;

    /// <summary>Deterministic so seed rows are stable across migrations.</summary>
    public static string Hash(string password, string salt)
    {
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, saltBytes, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256${Iterations}${salt}${Convert.ToBase64String(hash)}";
    }
}

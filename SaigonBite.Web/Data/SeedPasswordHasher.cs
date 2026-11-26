using System.Security.Cryptography;
using System.Text;

namespace SaigonBite.Web.Data;

/// <summary>
/// SEED ONLY. Deterministic salt makes HasData stable across migrations.
/// Real registration must use ASP.NET Core PasswordHasher&lt;TUser&gt; with a fresh random salt (BR18).
/// </summary>
public static class SeedPasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password, string salt)
    {
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, saltBytes, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256${Iterations}${salt}${Convert.ToBase64String(hash)}";
    }
}

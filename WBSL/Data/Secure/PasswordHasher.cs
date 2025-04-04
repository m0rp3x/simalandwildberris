using System.Security.Cryptography;
namespace WBSL.Data.Secure;

public static class PasswordHasher
{
    private const int SaltSize = 16; // 128 бит
    private const int KeySize = 32;  // 256 бит
    private const int Iterations = 100_000; // Количество итераций
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    // Метод для хеширования пароля
    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    // Метод для проверки введенного пароля с сохраненным хешем
    public static bool VerifyPassword(string password, string hashedPassword)
    {
        // Ожидаем формат: iterations.salt.hash
        string[] parts = hashedPassword.Split('.', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        int iterations = int.Parse(parts[0]);
        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] key = Convert.FromBase64String(parts[2]);

        // Вычисляем хеш для введенного пароля, используя ту же соль и итерации
        byte[] keyToCheck = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithm, key.Length);

        // Сравнение в режиме, защищенном от атак по времени
        return CryptographicOperations.FixedTimeEquals(key, keyToCheck);
    }
}
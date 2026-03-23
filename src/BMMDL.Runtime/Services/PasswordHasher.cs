namespace BMMDL.Runtime.Services;

/// <summary>
/// Service for hashing and verifying passwords using BCrypt.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hash a password.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verify a password against a hash.
    /// </summary>
    bool Verify(string password, string hash);
}

/// <summary>
/// BCrypt-based password hasher implementation.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <inheritdoc />
    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Split.Domain.Tests;

/// <summary>
/// The domain identifies participants by <see cref="Guid"/>, which makes for unreadable tests.
/// This maps a name to a stable id so the tests can talk about Aisyah and Bala, and still get
/// the same ids on every run — which matters, because the settlement output is ordered by id
/// when amounts tie.
/// </summary>
public static class People
{
    public static readonly Guid Aisyah = Of("aisyah");
    public static readonly Guid Bala = Of("bala");
    public static readonly Guid Chong = Of("chong");
    public static readonly Guid Dinesh = Of("dinesh");
    public static readonly Guid Elena = Of("elena");
    public static readonly Guid Farid = Of("farid");

    public static Guid Of(string name)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Numbered people, for tests that need a crowd.</summary>
    public static Guid Numbered(int index) => Of($"person-{index:D4}");
}

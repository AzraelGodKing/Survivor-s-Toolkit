using System.Security.Cryptography;
using System.Text;

namespace SurvivorsToolkit.Core;

/// <summary>
/// Passwords in the settings file: kept encrypted for the Windows user who typed them (DPAPI), never plain text.
/// A password encrypted for one Windows account can't be read by another, so an unreadable one comes back empty.
/// </summary>
public static class Secrets
{
    /// <summary>Encrypts a password for this Windows user; "" for an empty one.</summary>
    public static string? Protect(string password)
    {
        if (password.Length == 0 || !OperatingSystem.IsWindows()) return null;
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException) { return null; }
    }

    /// <summary>The password back, or "" when there is none or it was encrypted for another Windows account.</summary>
    public static string Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword) || !OperatingSystem.IsWindows()) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException or PlatformNotSupportedException) { return ""; }
    }
}

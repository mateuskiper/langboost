using LangBoost;
using Xunit;

namespace LangBoost.Tests;

/// <summary>
/// Regression tests for the DPAPI protection of the API key. Protect/Unprotect run under the
/// current Windows user (DataProtectionScope.CurrentUser) and do not touch %APPDATA%.
/// </summary>
public class AppConfigCryptoTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTripsOriginalValue()
    {
        const string secret = "AIza-some-fake-gemini-key_1234567890";

        string cipher = AppConfig.Protect(secret);
        string? plain = AppConfig.Unprotect(cipher);

        Assert.NotEqual(secret, cipher);          // actually encrypted
        Assert.Equal(secret, plain);              // decrypts back
    }

    [Fact]
    public void Protect_ProducesValidBase64()
    {
        string cipher = AppConfig.Protect("anything");

        // Should not throw — Save() persists this string verbatim into JSON.
        var bytes = Convert.FromBase64String(cipher);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Unprotect_InvalidBase64_ReturnsNull()
    {
        Assert.Null(AppConfig.Unprotect("not-valid-base64-$$$"));
    }

    [Fact]
    public void Unprotect_WellFormedBase64ButNotDpapi_ReturnsNull()
    {
        // Valid base64, but not produced by ProtectedData.Protect -> treated as "no key".
        string fake = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });

        Assert.Null(AppConfig.Unprotect(fake));
    }
}

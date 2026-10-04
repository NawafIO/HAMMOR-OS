using HAMMOR.Core.Diagnostics;
using Xunit;

namespace HAMMOR.Core.Tests;

/// <summary>
/// Verifies the "no secrets in logs" requirement at the point it is enforced.
/// </summary>
public sealed class SecretRedactorTests
{
    [Fact]
    public void Redacts_anthropic_style_keys()
    {
        const string key = "sk-ant-api03-AbCdEf1234567890XyZ_-abcdefghij";

        var result = SecretRedactor.Redact($"Request failed with key {key} attached.");

        Assert.DoesNotContain(key, result, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Mask, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacts_generic_sk_prefixed_keys()
    {
        const string key = "sk-1234567890abcdefghijklmnop";

        var result = SecretRedactor.Redact($"Authorization: Bearer {key}");

        Assert.DoesNotContain(key, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacts_elevenlabs_api_key_header()
    {
        var result = SecretRedactor.Redact("xi-api-key: 0123456789abcdef0123456789abcdef");

        Assert.DoesNotContain("0123456789abcdef", result, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Mask, result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("api_key=supersecretvalue123")]
    [InlineData("api-key: supersecretvalue123")]
    [InlineData("password = supersecretvalue123")]
    [InlineData("token: supersecretvalue123")]
    [InlineData("x-api-key: supersecretvalue123")]
    public void Redacts_labelled_secrets(string input)
    {
        var result = SecretRedactor.Redact(input);

        Assert.DoesNotContain("supersecretvalue123", result, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Mask, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_ordinary_text_untouched()
    {
        const string message = "Saved memory 'Project kickoff notes' to 2026-10/project-kickoff.md";

        Assert.Equal(message, SecretRedactor.Redact(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Handles_null_and_empty_input(string? input) =>
        Assert.Equal(string.Empty, SecretRedactor.Redact(input));

    [Fact]
    public void DescribePresence_never_reveals_the_value()
    {
        const string secret = "sk-ant-api03-SensitiveValue";

        var described = SecretRedactor.DescribePresence(secret);

        Assert.DoesNotContain("Sensitive", described, StringComparison.Ordinal);
        Assert.Contains(secret.Length.ToString(), described, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DescribePresence_reports_absence(string? secret) =>
        Assert.Equal("not set", SecretRedactor.DescribePresence(secret));
}

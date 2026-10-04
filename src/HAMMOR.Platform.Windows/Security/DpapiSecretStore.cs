using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using HAMMOR.Core.Security;
using HAMMOR.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Platform.Windows.Security;

/// <summary>
/// Stores credentials as DPAPI-encrypted files under the HAMMOR data folder,
/// scoped to the current Windows user.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DataProtectionScope.CurrentUser"/> means the ciphertext is only
/// decryptable by the logged-in user on this machine — copying the file to
/// another account or machine yields nothing useful.
/// </para>
/// <para>
/// Secrets never enter <c>hammor.config.json</c> and the whole data folder is
/// outside the repository, so there is no path by which a key reaches Git.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(
    HammorPaths paths,
    ILogger<DpapiSecretStore> logger) : ISecretStore
{
    /// <summary>
    /// Additional entropy mixed into the DPAPI blob. Not a secret — it scopes
    /// the ciphertext to HAMMOR so another application running as the same
    /// user cannot decrypt these files by pointing DPAPI at them.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HAMMOR.SecretStore.v1");

    private readonly HammorPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly ILogger<DpapiSecretStore> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(name);

        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var ciphertext = File.ReadAllBytes(path);
            var plaintext = ProtectedData.Unprotect(
                ciphertext, Entropy, DataProtectionScope.CurrentUser);

            return Task.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException ex)
        {
            // Wrong user, different machine, or a corrupted blob. Reported as
            // an error (not silently treated as "no secret") because the user
            // needs to know their stored key is unusable and must be re-entered.
            _logger.LogError(
                ex,
                "Could not decrypt secret '{Name}'. It may have been created by a different "
                + "Windows user or on a different machine; re-enter it in Settings.",
                name);

            return Task.FromResult<string?>(null);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not read secret '{Name}'.", name);
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var path = ResolvePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var ciphertext = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);

        // Temp-then-move so an interrupted write cannot truncate an existing
        // working credential.
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, ciphertext);
        File.Move(temp, path, overwrite: true);

        // Logs the name only — never the value.
        _logger.LogInformation("Stored secret '{Name}'.", name);

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(name);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Deleted secret '{Name}'.", name);
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not delete secret '{Name}'.", name);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolvePath(name)));

    /// <summary>
    /// Maps a secret name to a file path, rejecting anything that could escape
    /// the secrets directory.
    /// </summary>
    private string ResolvePath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Secret names are internal constants, but validating anyway means a
        // future caller cannot turn this into a path-traversal write.
        foreach (var ch in name)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-'))
            {
                throw new ArgumentException(
                    $"Secret name '{name}' contains unsupported character '{ch}'. "
                    + "Allowed: letters, digits, '.', '_', '-'.",
                    nameof(name));
            }
        }

        return Path.Combine(_paths.SecretsDirectory, name + ".dpapi");
    }
}

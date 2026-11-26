using System.IO.Compression;
using System.Security.Cryptography;

namespace ClaudeChatBackup.Infrastructure;

/// <summary>Opens a backup archive, reads its manifest and checks every file against its SHA-256.</summary>
public sealed class ArchiveReader : IDisposable
{
    readonly ZipArchive _zip;

    public BackupManifest Manifest { get; }
    public string ZipPath { get; }

    ArchiveReader(string zipPath, ZipArchive zip, BackupManifest manifest)
    {
        ZipPath = zipPath;
        _zip = zip;
        Manifest = manifest;
    }

    public static ArchiveReader Open(string zipPath)
    {
        var zip = ZipFile.OpenRead(zipPath);
        try
        {
            var entry = zip.GetEntry(DataLayout.ManifestEntry)
                ?? throw new InvalidDataException("Arquivo não é um backup do ClaudeChatBackup (manifest.json ausente).");
            BackupManifest manifest;
            using (var s = entry.Open()) manifest = ManifestSerializer.Read(s);

            if (manifest.FormatVersion > BackupManifest.CurrentFormatVersion)
                throw new InvalidDataException(
                    $"Backup no formato {manifest.FormatVersion}; esta versão lê até o formato {BackupManifest.CurrentFormatVersion}.");

            foreach (var f in manifest.Files)
                if (!DataLayout.IsSafeArchivePath(f.Path))
                    throw new InvalidDataException($"Caminho inseguro no backup: {f.Path}");

            return new ArchiveReader(zipPath, zip, manifest);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    public Stream OpenFile(ManifestFile file) =>
        (_zip.GetEntry(file.Path) ?? throw new InvalidDataException($"Arquivo ausente no backup: {file.Path}")).Open();

    /// <summary>
    /// Backups feitos antes da v0.6.0 guardavam o título das sessões de terminal cortado em 80
    /// caracteres. O texto completo está no transcript dentro do próprio zip: relê o primeiro pedido
    /// e troca o título cortado (só na memória; o arquivo não é alterado).
    /// </summary>
    public void RecoverFullTitles()
    {
        foreach (var s in Manifest.Sessions)
        {
            if (s.CliSessionId is not { } cli || s.Title is not { } title || !title.EndsWith('…')) continue;
            var file = Manifest.Files.FirstOrDefault(f =>
                f.Path.StartsWith(DataLayout.DotClaudePrefix + DataLayout.Projects + "/", StringComparison.Ordinal)
                && f.Path.EndsWith("/" + cli + ".jsonl", StringComparison.OrdinalIgnoreCase));
            if (file is null) continue;
            try
            {
                using var stream = OpenFile(file);
                if (SessionCatalog.FirstPrompt(stream) is { } full && full.Length >= title.Length - 1) s.Title = full;
            }
            catch (InvalidDataException)
            {
                // entrada ilegível: mantém o título que já existe
            }
        }
    }

    /// <summary>Returns one message per problem; an empty list means the archive is intact.</summary>
    public IReadOnlyList<string> Verify(IProgress<string>? progress = null)
    {
        var problems = new List<string>();
        foreach (var f in Manifest.Files)
        {
            progress?.Report(f.Path);
            var entry = _zip.GetEntry(f.Path);
            if (entry is null)
            {
                problems.Add($"ausente: {f.Path}");
                continue;
            }
            using var s = entry.Open();
            var (size, sha) = Hash(s);
            if (size != f.Size || !sha.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                problems.Add($"hash diferente: {f.Path}");
        }
        return problems;
    }

    public static (long Size, string Sha256) Hash(Stream s)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 16];
        long size = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, n);
            size += n;
        }
        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public void Dispose() => _zip.Dispose();
}

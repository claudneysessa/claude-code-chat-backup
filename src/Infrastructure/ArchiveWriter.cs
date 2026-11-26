using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClaudeChatBackup.Infrastructure;

/// <summary>A file on disk and the path it gets inside the archive.</summary>
public sealed record ArchiveItem(string SourcePath, string ArchivePath);

/// <summary>Writes backup archives: the files plus a manifest with a SHA-256 per file.</summary>
public static class ArchiveWriter
{
    const int BufferSize = 1 << 16;

    public static BackupManifest Write(
        string zipPath, BackupManifest manifest, IEnumerable<ArchiveItem> items, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        var partial = zipPath + ".partial";
        manifest.Files.Clear();

        try
        {
            using (var fs = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var item in items)
                {
                    if (!DataLayout.IsSafeArchivePath(item.ArchivePath))
                        throw new InvalidOperationException($"Caminho inválido no arquivo: {item.ArchivePath}");
                    if (!File.Exists(item.SourcePath)) continue;

                    progress?.Report(item.ArchivePath);
                    var entry = WriteEntry(zip, item);
                    if (entry is not null) manifest.Files.Add(entry);
                }

                var manifestEntry = zip.CreateEntry(DataLayout.ManifestEntry, CompressionLevel.Optimal);
                using var ms = manifestEntry.Open();
                ManifestSerializer.Write(ms, manifest);
            }

            File.Move(partial, zipPath, overwrite: true);
            return manifest;
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    static ManifestFile? WriteEntry(ZipArchive zip, ArchiveItem item)
    {
        FileStream src;
        try
        {
            src = SessionCatalog.OpenShared(item.SourcePath);
        }
        catch (FileNotFoundException)
        {
            return null; // deleted between listing and copying
        }

        using (src)
        {
            var lastWrite = File.GetLastWriteTimeUtc(item.SourcePath);
            // A transcript may be mid-write: keep only complete lines.
            var length = item.SourcePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? CompleteJsonLinesLength(src)
                : src.Length;

            var entry = zip.CreateEntry(item.ArchivePath, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(lastWrite, DateTimeKind.Utc));

            using var dst = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            src.Position = 0;
            var buffer = new byte[BufferSize];
            long remaining = length;
            while (remaining > 0)
            {
                var n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (n == 0) break;
                hash.AppendData(buffer, 0, n);
                dst.Write(buffer, 0, n);
                remaining -= n;
            }

            return new ManifestFile(
                item.ArchivePath, length - remaining, Convert.ToHexStringLower(hash.GetHashAndReset()), lastWrite);
        }
    }

    /// <summary>
    /// Length up to and including the last complete line. A trailing fragment without '\n' is kept
    /// only when it parses as JSON (a finished last line that simply lacks the newline).
    /// </summary>
    public static long CompleteJsonLinesLength(Stream s)
    {
        var len = s.Length;
        if (len == 0) return 0;

        var buffer = new byte[BufferSize];
        var pos = len;
        long lineEnd = 0;
        while (pos > 0)
        {
            var n = (int)Math.Min(buffer.Length, pos);
            pos -= n;
            s.Position = pos;
            s.ReadExactly(buffer, 0, n);
            var i = Array.LastIndexOf(buffer, (byte)'\n', n - 1, n);
            if (i >= 0)
            {
                lineEnd = pos + i + 1;
                break;
            }
        }

        if (lineEnd == len) return len;

        var tail = new byte[len - lineEnd];
        s.Position = lineEnd;
        s.ReadExactly(tail);
        return IsJson(tail) ? len : lineEnd;
    }

    static bool IsJson(byte[] bytes)
    {
        try
        {
            using var _ = JsonDocument.Parse(bytes);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Requires .NET 7/8: System.IO.Hashing
using System.IO.Hashing;

namespace FileSizeAnalyzer.Duplicates
{
    /// <summary>
    /// Result model for a group of duplicate files.
    /// </summary>
    public sealed class DuplicateGroup
    {
        public string DisplayName { get; init; } = "";
        public long FileSize { get; init; }
        public int Count => Paths?.Count ?? 0;
        public List<string> Paths { get; init; } = new(); // absolute file paths
        public long TotalBytes => FileSize * Count;
    }

    /// <summary>
    /// High-performance duplicate finder:
    /// Groups by size ⇒ partial hash (head/middle/tail) ⇒ full xxHash64 ⇒ optional byte-verify.
    /// Uses bounded parallelism and a persistent hash cache in %LocalAppData%.
    /// </summary>
    public static class DuplicatesEngine
    {
        public sealed class Options
        {
            /// <summary> Ignore files smaller than this threshold. Default = 256 KB. </summary>
            public long MinSizeBytes { get; set; } = 256 * 1024;

            /// <summary> If true, confirm equal hashes with a byte-by-byte compare (slower, safer). Default = false. </summary>
            public bool VerifyByteByByte { get; set; } = false;

            /// <summary> Degree of parallelism (defaults to half the logical cores, min 1). </summary>
            public int? MaxDegreeOfParallelism { get; set; } = null;

            /// <summary> For files larger than this, force verify regardless of VerifyByteByByte (safety valve). Default = 32 MB. </summary>
            public long ForceVerifyAboveBytes { get; set; } = 32L * 1024 * 1024;
        }

        /// <summary>
        /// Finds duplicate files from a sequence of absolute file paths.
        /// Reports each discovered duplicate group via progress (optional), and returns all groups on completion.
        /// </summary>
        public static async Task<List<DuplicateGroup>> FindDuplicatesAsync(
            IEnumerable<string> filePaths,
            Options options,
            IProgress<DuplicateGroup>? progress = null,
            CancellationToken token = default)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            options ??= new Options();

            var files = filePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    try
                    {
                        var fi = new FileInfo(p);
                        if (!fi.Exists) return null;
                        return fi;
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(fi => fi != null && fi!.Length >= options.MinSizeBytes)
                .ToList()!;

            return await FindDuplicatesAsync(files, options, progress, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds duplicates from a sequence of FileInfo objects.
        /// </summary>
        public static Task<List<DuplicateGroup>> FindDuplicatesAsync(
            IEnumerable<FileInfo> files,
            Options options,
            IProgress<DuplicateGroup>? progress = null,
            CancellationToken token = default)
        {
            options ??= new Options();
            var degree = Math.Max(1, options.MaxDegreeOfParallelism ?? (Environment.ProcessorCount / 2));

            // 1) Group by file size
            var bySize = files
                .Where(fi => fi != null && fi.Exists && fi.Length >= options.MinSizeBytes)
                .GroupBy(fi => fi.Length)
                .Where(g => g.Count() >= 2)
                .ToList();

            var allGroups = new ConcurrentBag<DuplicateGroup>();
            var cache = new HashCache();

            foreach (var sizeGroup in bySize)
            {
                token.ThrowIfCancellationRequested();

                // 2) PARTIAL HASH (head/middle/tail sampling)
                var byPartial = new ConcurrentDictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

                Parallel.ForEach(sizeGroup,
                    new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = token },
                    fi =>
                    {
                        var ph = ComputePartialHash3Windows(fi.FullName);
                        if (string.IsNullOrEmpty(ph)) return;

                        var list = byPartial.GetOrAdd(ph!, _ => new List<FileInfo>());
                        lock (list) list.Add(fi);
                    });

                foreach (var phGroup in byPartial.Values)
                {
                    token.ThrowIfCancellationRequested();
                    if (phGroup.Count < 2) continue;

                    // 3) FULL HASH (xxHash64) with cache
                    var byFull = new ConcurrentDictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

                    Parallel.ForEach(phGroup,
                        new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = token },
                        fi =>
                        {
                            var fh = ComputeFastHash(fi, cache);
                            if (string.IsNullOrEmpty(fh)) return;
                            var list = byFull.GetOrAdd(fh!, _ => new List<FileInfo>());
                            lock (list) list.Add(fi);
                        });

                    // 4) Optional BYTE VERIFY; create groups and report
                    foreach (var fhGroup in byFull.Values)
                    {
                        token.ThrowIfCancellationRequested();
                        if (fhGroup.Count < 2) continue;

                        // Option: treat each hash group as one duplicate group,
                        // optionally verifying against the first file.
                        var anchor = fhGroup[0];
                        var anchorPath = anchor.FullName;

                        var verified = new List<FileInfo> { anchor };

                        for (int i = 1; i < fhGroup.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var candidate = fhGroup[i];

                            bool mustVerify = options.VerifyByteByByte || anchor.Length >= options.ForceVerifyAboveBytes;

                            if (!mustVerify || AreFilesIdentical(anchorPath, candidate.FullName))
                                verified.Add(candidate);
                        }

                        if (verified.Count >= 2)
                        {
                            var group = new DuplicateGroup
                            {
                                DisplayName = anchor.Name,
                                FileSize = anchor.Length,
                                Paths = verified.Select(v => v.FullName).ToList()
                            };

                            allGroups.Add(group);
                            progress?.Report(group);
                        }
                    }
                }
            }

            // Persist cache for next runs
            cache.Save();

            return Task.FromResult(allGroups.OrderByDescending(g => g.TotalBytes).ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
        }

        // =========================
        //        Internals
        // =========================

        private static FileStream OpenFastReadStream(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                           bufferSize: 128 * 1024, options: FileOptions.SequentialScan);

        private static string ComputeFastHash(FileInfo fi, HashCache cache)
        {
            try
            {
                var mtimeUtc = fi.LastWriteTimeUtc;
                if (cache.TryGet(fi.FullName, fi.Length, mtimeUtc, out var cached))
                    return cached;

                using var stream = OpenFastReadStream(fi.FullName);
                if (stream.Length == 0)
                {
                    cache.Put(fi.FullName, 0, mtimeUtc, "EMPTY");
                    return "EMPTY";
                }

                var hasher = new XxHash64();
                hasher.Append(stream);
                var hash = ToHex(hasher.GetHashAndReset());
                cache.Put(fi.FullName, fi.Length, mtimeUtc, hash);
                return hash;
            }
            catch
            {
                // Return unique to avoid false grouping; errors ignored but logged upstream if needed
                return Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>
        /// Partial hash sampling: reads 3 windows (head/middle/tail), 64 KB each, hashed with xxHash64.
        /// </summary>
        private static string? ComputePartialHash3Windows(string path)
        {
            try
            {
                using var stream = OpenFastReadStream(path);
                if (stream.Length == 0) return "EMPTY";
                if (stream.Length <= 256 * 1024)
                {
                    // For small files, just full fast hash to reduce false collisions.
                    var hasherSmall = new XxHash64();
                    hasherSmall.Append(stream);
                    return ToHex(hasherSmall.GetHashAndReset());
                }

                const int window = 64 * 1024;
                var hasher = new XxHash64();

                Span<byte> buffer = stackalloc byte[0]; // Avoid stackalloc of 64k; allocate via ArrayPool
                byte[] rented = ArrayPool<byte>.Shared.Rent(window);
                try
                {
                    long[] offsets =
                    {
                        0,
                        Math.Max(0, (stream.Length / 2) - window / 2),
                        Math.Max(0, stream.Length - window)
                    };

                    foreach (var off in offsets)
                    {
                        stream.Position = off;
                        int read = stream.Read(rented, 0, window);
                        if (read > 0) hasher.Append(new ReadOnlySpan<byte>(rented, 0, read));
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }

                return ToHex(hasher.GetHashAndReset());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Safe byte-by-byte equality check using a 256 KB buffer.
        /// </summary>
        public static bool AreFilesIdentical(string path1, string path2)
        {
            if (string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase)) return true;

            FileInfo f1, f2;
            try
            {
                f1 = new FileInfo(path1);
                f2 = new FileInfo(path2);
            }
            catch { return false; }

            if (!f1.Exists || !f2.Exists) return false;
            if (f1.Length != f2.Length) return false;

            const int bufSize = 256 * 1024;
            var buf1 = ArrayPool<byte>.Shared.Rent(bufSize);
            var buf2 = ArrayPool<byte>.Shared.Rent(bufSize);
            try
            {
                using var s1 = OpenFastReadStream(path1);
                using var s2 = OpenFastReadStream(path2);

                while (true)
                {
                    int r1 = s1.Read(buf1, 0, bufSize);
                    int r2 = s2.Read(buf2, 0, bufSize);
                    if (r1 != r2) return false;
                    if (r1 == 0) return true; // EOF for both

                    // Compare chunk
                    for (int i = 0; i < r1; i++)
                    {
                        if (buf1[i] != buf2[i]) return false;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf1);
                ArrayPool<byte>.Shared.Return(buf2);
            }
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");

        /// <summary>
        /// Simple persistent hash cache keyed by full path + (size, mtime).
        /// </summary>
        private sealed class HashCache
        {
            private readonly string _cachePath =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "FileSizeAnalyzer", "hashCache.db");

            private readonly Dictionary<string, (long size, DateTime mtimeUtc, string hash)> _map =
                new(StringComparer.OrdinalIgnoreCase);

            public HashCache()
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                    if (!File.Exists(_cachePath)) return;
                    foreach (var line in File.ReadAllLines(_cachePath))
                    {
                        var parts = line.Split('|');
                        if (parts.Length != 4) continue;
                        if (!long.TryParse(parts[1], out var size)) continue;
                        if (!long.TryParse(parts[2], out var ticks)) continue;
                        _map[parts[0]] = (size, new DateTime(ticks, DateTimeKind.Utc), parts[3]);
                    }
                }
                catch { /* ignore cache load errors */ }
            }

            public bool TryGet(string path, long size, DateTime mtimeUtc, out string hash)
            {
                if (_map.TryGetValue(path, out var v) && v.size == size && v.mtimeUtc == mtimeUtc)
                {
                    hash = v.hash; return true;
                }
                hash = null!;
                return false;
            }

            public void Put(string path, long size, DateTime mtimeUtc, string hash)
            {
                _map[path] = (size, mtimeUtc, hash);
            }

            public void Save()
            {
                try
                {
                    using var w = new StreamWriter(_cachePath, false, Encoding.UTF8);
                    foreach (var kvp in _map)
                    {
                        var (size, mt, h) = kvp.Value;
                        w.WriteLine($"{kvp.Key}|{size}|{mt.Ticks}|{h}");
                    }
                }
                catch { /* ignore cache save errors */ }
            }
        }
    }
}
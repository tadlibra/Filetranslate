using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Filetranslate;

public partial class Form1
{
    private async Task StartTranslateAsync()
    {
        if (_cts is not null) return;
        var instancePath = _pathBox.Text.Trim();
        if (!Directory.Exists(instancePath) || !ContentRoots.Any(x => Directory.Exists(Path.Combine(instancePath, x))))
        {
            Log("Không tìm thấy thư mục modpack (mods/config/kubejs/resourcepacks).");
            return;
        }
        var (langCode, langFile) = Languages[_langBox.Text];
        _resetTranslations = _resetBox.Checked;
        _cts = new CancellationTokenSource();
        _startButton.Enabled = _settingsButton.Enabled = _pathBox.Enabled = _langBox.Enabled = _resetBox.Enabled = false;
        _stopButton.Enabled = true;
        _logBox.Clear();
        _errorCount = _doneJobs = _totalJobs = _aiUseLogged = _aiFallbackLogged = 0;
        _coverage.Clear();
        _stopwatch = Stopwatch.StartNew();
        _uiTimer.Start();
        SetStatus("Đang quét...");
        try
        {
            await Task.Run(() => RunTranslateAsync(instancePath, langCode, langFile, _cts.Token));
            SetStatus(_errorCount > 0 ? $"Xong lượt dịch - {_errorCount} lỗi" : "Xong lượt dịch - xem báo cáo độ phủ");
        }
        catch (OperationCanceledException) { Log("Đã dừng; file đang xử lý giữ nguyên."); SetStatus("Đã dừng"); }
        catch (Exception ex) { Log($"Lỗi: {ex.Message}"); SetStatus("Lỗi"); }
        finally
        {
            _uiTimer.Stop();
            _stopwatch?.Stop();
            _cts.Dispose();
            _cts = null;
            _startButton.Enabled = _settingsButton.Enabled = _pathBox.Enabled = _langBox.Enabled = _resetBox.Enabled = true;
            _stopButton.Enabled = false;
        }
    }

    private void StopTranslate() => _cts?.Cancel();

    private async Task RunTranslateAsync(string instancePath, string langCode, string langFile, CancellationToken ct)
    {
        var staging = SafeChildPath(instancePath, ".beeslator-staging");
        Directory.CreateDirectory(staging);
        var complete = false;
        Log("Quét JAR/ZIP, lang, Patchouli, nhiệm vụ và datapack. Mỗi file được backup trước khi thay thế.");
        try
        {
            var files = FindContentFiles(instancePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var jobs = new List<(string Name, Func<Task<int>> Action)>();
            foreach (var archive in files.Where(IsArchive))
                jobs.Add((archive, () => ProcessArchiveAsync(archive, NormalizePath(Path.GetRelativePath(instancePath, archive)), staging, langCode, langFile, 0, ct)));

            var groups = files.Where(f => LangOutputRelative(NormalizePath(Path.GetRelativePath(instancePath, f)), langFile) is not null)
                .GroupBy(f => LangOutputRelative(NormalizePath(Path.GetRelativePath(instancePath, f)), langFile)!, StringComparer.OrdinalIgnoreCase);
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var sources = group.ToArray();
                foreach (var file in sources) handled.Add(file);
                var targetRel = group.Key;
                jobs.Add((targetRel, async () =>
                {
                    var content = new List<(string Name, string Content)>();
                    foreach (var file in sources) content.Add((file, await File.ReadAllTextAsync(file, ct)));
                    var target = SafeChildPath(instancePath, targetRel);
                    var existing = File.Exists(target) ? await File.ReadAllTextAsync(target, ct) : null;
                    var result = await TranslateLangGroupAsync(targetRel, staging, content, existing, targetRel, langCode, ct);
                    if (result.Errors == 0 && existing != result.Content) await WriteAtomicAsync(target, result.Content, true, ct);
                    if (result.Errors == 0) await WriteAtomicAsync(result.StatePath, result.StateContent, false, ct);
                    return result.Errors;
                }));
            }
            var documentTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.Where(f => !IsArchive(f) && !handled.Contains(f)).OrderBy(SourceRank).ThenBy(f => f, StringComparer.Ordinal))
            {
                var relative = NormalizePath(Path.GetRelativePath(instancePath, file));
                var targetRel = DocumentOutputRelative(relative, langFile);
                if (targetRel is null)
                {
                    if (IsReviewCandidate(relative) && !IsKnownLocaleResource(relative))
                        _coverage.Enqueue(new(relative, "manual-review", 0, 0, "No supported text adapter; content may contain visible text."));
                    continue;
                }
                if (!documentTargets.Add(targetRel)) continue;
                jobs.Add((relative, async () =>
                {
                    var target = SafeChildPath(instancePath, targetRel);
                    var raw = await File.ReadAllTextAsync(file, ct);
                    var (original, statePath) = await ReadDocumentOriginalAsync(relative, staging, raw, ct);
                    var result = await TranslateDocumentAsync(relative, relative, original, staging, langCode, ct);
                    if (file != target || raw != result.Content)
                    {
                        // Do not commit a partly translated quest document; successful strings remain cached.
                        if (result.Errors == 0)
                        {
                            await WriteAtomicAsync(target, result.Content, true, ct);
                            if (file == target) await SaveDocumentStateAsync(statePath, original, result.Content, ct);
                        }
                    }
                    return result.Errors;
                }));
            }
            _totalJobs = jobs.Count;
            UpdateProgress();
            Log($"Tìm thấy {files.Count(IsArchive)} archive; {_totalJobs} việc dịch. Các file chưa hỗ trợ được ghi vào báo cáo.");
            await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
                async (job, token) => await RunJobAsync(job.Name, job.Action, token));
            ct.ThrowIfCancellationRequested();
            await SetMinecraftLanguageAsync(instancePath, langFile, ct);
            complete = true;
            Log($"Xong lượt dịch: {_errorCount} lỗi; thời gian {_stopwatch?.Elapsed:mm\\:ss}.");
        }
        finally
        {
            var entries = _coverage.ToArray();
            var report = new
            {
                Completed = complete,
                Scope = "Supported static localization resources; does not prove complete in-game translation.",
                GeneratedAtUtc = DateTime.UtcNow,
                Errors = _errorCount,
                ManualReviewFiles = entries.Count(x => x.Status == "manual-review"),
                UnchangedStrings = entries.Sum(x => x.Unchanged),
                Entries = entries
            };
            var reportPath = SafeChildPath(staging, "coverage-report.json");
            await WriteAtomicAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions(true)), false, CancellationToken.None);
            Log($"Báo cáo độ phủ: {reportPath}");
        }
    }

    private async Task RunJobAsync(string name, Func<Task<int>> job, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Add(ref _errorCount, await job());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (NotSupportedException ex) { _coverage.Enqueue(new(name, "manual-review", 0, 0, ex.Message)); }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _coverage.Enqueue(new(name, "error", 0, 0, ex.Message));
            Log($"{Path.GetFileName(name)}: {ex.Message}");
        }
        finally
        {
            Interlocked.Increment(ref _doneJobs);
            UpdateProgress();
        }
    }

    private static bool IsKnownLocaleResource(string path) => System.Text.RegularExpressions.Regex.IsMatch(path,
        @"(?:/lang/[^/]+\.(?:json|lang|snbt|json5)$|/(?:en_us|vi_vn|zh_cn|zh_tw|ja_jp|ko_kr)/)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private const long MaxTextBytes = 16 * 1024 * 1024;
    private const long MaxNestedArchiveBytes = 512 * 1024 * 1024;

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        if (entry.Length > MaxTextBytes) throw new IOException($"Text entry exceeds 16 MiB: {entry.FullName}");
        using var reader = new StreamReader(entry.Open(), Utf8NoBom, true);
        return await reader.ReadToEndAsync(ct);
    }

    private async Task<int> ProcessArchiveAsync(string archivePath, string identity, string staging, string langCode, string langFile, int depth, CancellationToken ct)
    {
        if (depth > 6) throw new IOException("Nested archive depth exceeds 6.");
        archivePath = SafeChildPath(Path.GetDirectoryName(archivePath)!, Path.GetFileName(archivePath));
        var temporary = archivePath + ".beeslator-" + Guid.NewGuid().ToString("N") + ".tmp";
        var errors = 0;
        var changed = false;
        var pendingStates = new List<(string Path, string Content)>();
        try
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(archivePath, temporary);
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                if (HasJarSignature(zip))
                {
                    _coverage.Enqueue(new(identity, "manual-review", 0, 0, "Signed archive: needs an external resource pack or mod-specific integration."));
                    return 0;
                }
                var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
                if (entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024) throw new IOException("Archive expansion exceeds 2 GiB.");
                if (entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new IOException("Archive contains duplicate or case-colliding paths.");
                foreach (var entry in entries) _ = SafeChildPath(staging, entry.FullName);
                var classCount = entries.Count(e => e.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase));
                if (classCount > 0) _coverage.Enqueue(new(identity, "manual-review", 0, 0, $"{classCount} Java classes: runtime-generated/hardcoded text not rewritten."));

                async Task ReplaceEntry(string name, string content)
                {
                    var old = zip.GetEntry(name);
                    if (old is not null && await ReadEntryAsync(old, ct) == content) return;
                    old?.Delete();
                    var replacement = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(replacement.Open(), Utf8NoBom);
                    await writer.WriteAsync(content.AsMemory(), ct);
                    changed = true;
                }

                var langGroups = entries.Where(e => LangOutputRelative(e.FullName, langFile) is not null)
                    .GroupBy(e => LangOutputRelative(e.FullName, langFile)!, StringComparer.OrdinalIgnoreCase).ToArray();
                var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in langGroups)
                {
                    foreach (var entry in group) handled.Add(entry.FullName);
                    try
                    {
                        var sources = new List<(string Name, string Content)>();
                        foreach (var entry in group)
                        {
                            try { sources.Add((entry.FullName, await ReadEntryAsync(entry, ct))); }
                            catch (Exception ex) when (ex is IOException or System.Text.DecoderFallbackException)
                            {
                                errors++;
                                _coverage.Enqueue(new(identity + "!" + entry.FullName, "source-error", 0, 0, ex.Message));
                            }
                        }
                        var existing = zip.GetEntry(group.Key);
                        var result = await TranslateLangGroupAsync(identity + "!" + group.Key, staging, sources,
                            existing is null ? null : await ReadEntryAsync(existing, ct), group.Key, langCode, ct);
                        if (result.Errors == 0)
                        {
                            await ReplaceEntry(group.Key, result.Content);
                            pendingStates.Add((result.StatePath, result.StateContent));
                        }
                        errors += result.Errors;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { errors++; _coverage.Enqueue(new(identity + "!" + group.Key, "error", 0, 0, ex.Message)); }
                }
                var documentTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries.Where(e => !handled.Contains(e.FullName)).OrderBy(e => SourceRank(e.FullName)))
                {
                    ct.ThrowIfCancellationRequested();
                    var entryName = entry.FullName;
                    var entryIdentity = identity + "!" + entryName;
                    try
                    {
                        if (IsArchive(entryName))
                        {
                            if (entry.Length > MaxNestedArchiveBytes) throw new IOException("Nested archive exceeds 512 MiB.");
                            var nested = SafeChildPath(staging, "nested/" + ContentHash(entryIdentity) + ".jar");
                            Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
                            using (var input = entry.Open())
                            using (var output = File.Create(nested)) await input.CopyToAsync(output, ct);
                            var before = new FileInfo(nested).Length;
                            var nestedHash = await HashFileAsync(nested, ct);
                            errors += await ProcessArchiveAsync(nested, entryIdentity, staging, langCode, langFile, depth + 1, ct);
                            if (new FileInfo(nested).Length != before || await HashFileAsync(nested, ct) != nestedHash)
                            {
                                entry.Delete();
                                using var input = File.OpenRead(nested);
                                using var output = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                                await input.CopyToAsync(output, ct);
                                changed = true;
                            }
                            File.Delete(nested);
                            continue;
                        }
                        var target = DocumentOutputRelative(entryName, langFile);
                        if (target is null)
                        {
                            if (IsReviewCandidate(entryName) && !IsKnownLocaleResource(entryName))
                                _coverage.Enqueue(new(entryIdentity, "manual-review", 0, 0, "No supported text adapter."));
                            continue;
                        }
                        if (!documentTargets.Add(target)) continue;
                        var raw = await ReadEntryAsync(entry, ct);
                        var (original, statePath) = await ReadDocumentOriginalAsync(entryIdentity, staging, raw, ct);
                        var result = await TranslateDocumentAsync(entryIdentity, entryName, original, staging, langCode, ct);
                        errors += result.Errors;
                        if (result.Errors == 0)
                        {
                            await ReplaceEntry(target, result.Content);
                            if (target == entryName) pendingStates.Add((statePath, JsonSerializer.Serialize(new DocumentCheckpoint(original, ContentHash(result.Content)))));
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (NotSupportedException ex) { _coverage.Enqueue(new(entryIdentity, "manual-review", 0, 0, ex.Message)); }
                    catch (Exception ex) { errors++; _coverage.Enqueue(new(entryIdentity, "error", 0, 0, ex.Message)); }
                }
            }
            ct.ThrowIfCancellationRequested();
            if (changed)
            {
                using (var check = ZipFile.OpenRead(temporary)) _ = check.Entries.Count;
                CommitFile(temporary, archivePath, depth == 0);
                _coverage.Enqueue(new(identity, "archive-written", 0, 0));
            }
            foreach (var state in pendingStates) await WriteAtomicAsync(state.Path, state.Content, false, ct);
            return errors;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct));
    }
}


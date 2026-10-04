namespace PersonalMediaPlayer.App.Download;

internal enum DownloadBatchOutcome
{
    Saved,
    Failed,
    Kept
}

internal enum DownloadReplaceChoice
{
    Replace,
    KeepExisting,
    Cancel
}

internal sealed record DownloadBatchFile(string Title, string SourcePath, bool AudioOnly);

internal sealed record DownloadSaveTarget(
    string SourcePath,
    string Title,
    string FileName,
    string DestinationPath,
    bool AlreadyThere);

internal sealed record DownloadBatchResult(
    string SourcePath,
    string Title,
    string FileName,
    DownloadBatchOutcome Outcome,
    string? SavedPath,
    string? Detail)
{
    public string SavedLabel => string.IsNullOrWhiteSpace(SavedPath) ? FileName : Path.GetFileName(SavedPath);
}

internal static class DownloadBatchSave
{
    public static string SavedName(string title, string sourcePath, bool audioOnly)
        => YoutubeDownloader.FileName(title, audioOnly) + ExtensionFor(sourcePath, audioOnly);

    public static IReadOnlyList<DownloadSaveTarget> Plan(IReadOnlyList<DownloadBatchFile> files, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var root = Path.GetFullPath(folder);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<DownloadSaveTarget>(files.Count);
        foreach (var file in files)
        {
            var fileName = Claim(root, file, used);
            var destination = Path.Combine(root, fileName);
            plans.Add(new DownloadSaveTarget(file.SourcePath, file.Title, fileName, destination, File.Exists(destination)));
        }

        return plans;
    }

    public static IReadOnlyList<DownloadBatchResult> CopyAll(IReadOnlyList<DownloadSaveTarget> targets, bool replaceExisting)
    {
        var results = new List<DownloadBatchResult>(targets.Count);
        foreach (var target in targets)
        {
            try
            {
                var existsNow = File.Exists(target.DestinationPath);
                if (existsNow && !replaceExisting)
                {
                    results.Add(Result(target, DownloadBatchOutcome.Kept, null, null));
                    continue;
                }

                if (existsNow && !target.AlreadyThere)
                {
                    results.Add(Result(target, DownloadBatchOutcome.Failed, null, "A file with that name is already in the folder."));
                    continue;
                }

                Copy(target.SourcePath, target.DestinationPath, replace: existsNow);
                results.Add(Result(target, DownloadBatchOutcome.Saved, target.DestinationPath, null));
            }
            catch (Exception ex)
            {
                results.Add(Result(target, DownloadBatchOutcome.Failed, null, Shorten(ex.Message)));
            }
        }

        return results;
    }

    public static void Copy(string source, string destination, bool replace)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new FileNotFoundException("That download is no longer there.", source);
        }

        var fullSource = Path.GetFullPath(source);
        var fullDestination = Path.GetFullPath(destination);
        if (string.Equals(fullSource, fullDestination, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Choose a different folder.");
        }

        var directory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException("Choose a folder.");
        }

        Directory.CreateDirectory(directory);
        var existed = File.Exists(fullDestination);
        if (existed && !replace)
        {
            throw new IOException("A file with that name is already in the folder.");
        }

        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".saving");
        try
        {
            File.Copy(fullSource, temporary, overwrite: false);
            File.Move(temporary, fullDestination, overwrite: existed);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static string Describe(IReadOnlyList<DownloadBatchResult> results, string place)
    {
        var saved = results.Where(item => item.Outcome == DownloadBatchOutcome.Saved).ToList();
        var failed = results.Where(item => item.Outcome == DownloadBatchOutcome.Failed).ToList();
        var kept = results.Where(item => item.Outcome == DownloadBatchOutcome.Kept).ToList();
        var parts = new List<string>();
        if (saved.Count == 1)
        {
            parts.Add($"Saved {saved[0].SavedLabel} to {place}.");
        }
        else if (saved.Count > 1)
        {
            parts.Add($"Saved {saved.Count} files to {place}: {string.Join(", ", saved.Select(item => item.SavedLabel))}.");
        }

        if (failed.Count == 1)
        {
            parts.Add($"Could not save {FailedText(failed[0])}. It is still ready.");
        }
        else if (failed.Count > 1)
        {
            parts.Add($"Could not save {string.Join(", ", failed.Select(FailedText))}. They are still ready.");
        }

        if (kept.Count > 0)
        {
            parts.Add("Left ready because a file with that name is already there: " + string.Join(", ", kept.Select(item => item.FileName)) + ".");
        }

        return string.Join(" ", parts);
    }

    public static string Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Could not save that file.";
        }

        var line = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim().TrimEnd('.');
        if (line.Length > 180)
        {
            line = line[..180].Trim() + "…";
        }

        return line.Length == 0 ? "Could not save that file." : line;
    }

    private static DownloadBatchResult Result(DownloadSaveTarget target, DownloadBatchOutcome outcome, string? savedPath, string? detail)
        => new(target.SourcePath, target.Title, target.FileName, outcome, savedPath, detail);

    private static string FailedText(DownloadBatchResult item)
        => string.IsNullOrWhiteSpace(item.Detail) ? item.Title : item.Title + " (" + item.Detail.Trim().TrimEnd('.') + ")";

    private static string Claim(string folder, DownloadBatchFile file, HashSet<string> used)
    {
        var extension = ExtensionFor(file.SourcePath, file.AudioOnly);
        var stem = YoutubeDownloader.FileName(file.Title, file.AudioOnly);
        var name = stem + extension;
        if (used.Add(Path.Combine(folder, name)))
        {
            return name;
        }

        for (var number = 2; ; number++)
        {
            name = $"{stem} ({number}){extension}";
            if (used.Add(Path.Combine(folder, name)))
            {
                return name;
            }
        }
    }

    private static string ExtensionFor(string sourcePath, bool audioOnly)
    {
        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
        {
            return extension.ToLowerInvariant();
        }

        return audioOnly ? ".m4a" : ".mp4";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

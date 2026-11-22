using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class StandaloneTrackSync : IPostprocessBuildWithReport
{
    private const string TracksFolder = "Tracks";
    private const string BackupFolder = "Tracks.Backup";
    private static readonly StringComparer WindowsPaths = StringComparer.OrdinalIgnoreCase;

    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64)
            return;

        try
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (Sync(project, report.summary.outputPath, out string backup))
            {
                Debug.Log("Synced project tracks to the Windows player folder.");
                if (backup != null)
                    Debug.Log($"Previous standalone tracks backed up to {backup}");
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new BuildFailedException($"Could not sync the standalone Tracks folder: {ex.Message}");
        }
    }

    internal static bool Sync(string projectDirectory, string playerPath, out string backup)
    {
        string output = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(projectDirectory, playerPath)));
        string source = Path.GetFullPath(Path.Combine(projectDirectory, TracksFolder));
        string destination = Path.Combine(output, TracksFolder);
        backup = null;

        if (WindowsPaths.Equals(source, destination))
            return false;

        if (IsInside(destination, source) || IsInside(source, destination))
            throw new IOException("The project and standalone Tracks folders must not overlap.");

        // Directory moves use the output volume. Reject junctions and symlinks
        // so the replacement cannot affect a different track catalog by alias.
        for (var directory = new DirectoryInfo(output); directory != null; directory = directory.Parent)
            CheckForLink(directory);
        for (var directory = new DirectoryInfo(source); directory != null; directory = directory.Parent)
            CheckForLink(directory);

        var projectEntries = ReadTree(source);
        bool hasDestination = Directory.Exists(destination);
        if (hasDestination && SameTree(projectEntries, ReadTree(destination)))
            return false;

        string token = Path.GetRandomFileName();
        string staged = Path.Combine(output, $".Tracks-{token}.pending");
        Directory.CreateDirectory(staged);
        try
        {
            foreach (var pair in projectEntries)
            {
                string target = Path.Combine(staged, pair.Key);
                if (pair.Value is DirectoryInfo)
                    Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(pair.Value.FullName, target);
                }
            }

            if (hasDestination)
            {
                string archiveRoot = Path.Combine(output, BackupFolder);
                if (Directory.Exists(archiveRoot))
                    CheckForLink(new DirectoryInfo(archiveRoot));
                Directory.CreateDirectory(archiveRoot);
                backup = Path.Combine(archiveRoot, $"{DateTime.UtcNow:yyMMdd-HHmmss}-{token}");
                MoveDirectory(destination, backup);
            }

            try
            {
                MoveDirectory(staged, destination);
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) &&
                backup != null && !Directory.Exists(destination) && !File.Exists(destination))
            {
                MoveDirectory(backup, destination);
                throw;
            }
            return true;
        }
        finally
        {
            if (Directory.Exists(staged))
            {
                try { Directory.Delete(staged, recursive: true); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"Could not remove temporary track directory {staged}: {ex.Message}");
                }
            }
        }
    }

    private static bool IsInside(string path, string parent) =>
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void MoveDirectory(string source, string destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception ex) when (attempt < 3 && (ex is IOException || ex is UnauthorizedAccessException)
                && Directory.Exists(source) && !Directory.Exists(destination) && !File.Exists(destination))
            {
                // A Windows file scanner can briefly retain a handle after copying.
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private static void CheckForLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 && entry.Exists)
            throw new IOException($"Track sync cannot use a symbolic link or junction: {entry.FullName}");
    }

    private static Dictionary<string, FileSystemInfo> ReadTree(string path)
    {
        var entries = new Dictionary<string, FileSystemInfo>(StringComparer.Ordinal);
        AddDirectory(new DirectoryInfo(path));
        return entries;

        void AddDirectory(DirectoryInfo directory)
        {
            CheckForLink(directory);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                CheckForLink(entry);
                entries.Add(Path.GetRelativePath(path, entry.FullName), entry);
                if (entry is DirectoryInfo child)
                    AddDirectory(child);
            }
        }
    }

    private static bool SameTree(Dictionary<string, FileSystemInfo> source, Dictionary<string, FileSystemInfo> destination)
    {
        if (source.Count != destination.Count)
            return false;

        using var hash = SHA256.Create();
        foreach (var pair in source)
        {
            if (!destination.TryGetValue(pair.Key, out var target) || target.GetType() != pair.Value.GetType())
                return false;
            if (pair.Value is FileInfo original)
            {
                var copy = (FileInfo)target;
                if (original.Length != copy.Length)
                    return false;
                using var first = original.OpenRead();
                using var second = copy.OpenRead();
                if (!hash.ComputeHash(first).SequenceEqual(hash.ComputeHash(second)))
                    return false;
            }
        }
        return true;
    }
}

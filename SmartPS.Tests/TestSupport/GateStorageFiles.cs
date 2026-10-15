namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Reads the gate's local storage files (Storage/offline_active_sessions.json and Storage/gate_audit_log.jsonl under the
/// test output directory) without blocking the service's writers.
/// <para>
/// <c>File.ReadAllText/ReadAllLines</c> open with <c>FileShare.Read</c>: while a test reads, a concurrent
/// <c>File.WriteAllText/AppendAllLines</c> of a gate service fails, and the service swallows the error, so a JSONL line or
/// an offline snapshot is silently lost; and a read that starts while a writer holds the file throws
/// "being used by another process". These helpers open with <c>FileShare.ReadWrite | FileShare.Delete</c> and retry briefly on
/// IOException. All test classes that drive a real GateControlService also run in <see cref="PostgresCollection"/> (no
/// parallelism), so only threads of a single test can race here.
/// </para>
/// </summary>
public static class GateStorageFiles
{
    public static string StorageDirectory => Path.Combine(AppContext.BaseDirectory, "Storage");

    public static string JsonlPath => Path.Combine(StorageDirectory, "gate_audit_log.jsonl");

    public static string OfflineSessionsPath => Path.Combine(StorageDirectory, "offline_active_sessions.json");

    /// <summary>File content, or an empty string when the file does not exist.</summary>
    public static string ReadText(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return string.Empty;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
            }
            catch (IOException) when (attempt < 40)
            {
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>File lines, or none when the file does not exist.</summary>
    public static string[] ReadLines(string path)
        => ReadText(path).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
}

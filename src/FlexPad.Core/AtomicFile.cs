namespace FlexPad.Core;

/// <summary>
/// Durable file helpers for small config writes. A plain <see cref="System.IO.File.WriteAllText(string,string)"/>
/// truncates the target before writing, so a crash or power loss mid-write can leave it empty or
/// half-written. Writing to a sibling temp file and atomically renaming it over the target means a
/// reader only ever sees the intact old file or the complete new one — never a torn one.
/// </summary>
public static class AtomicFile
{
    /// <summary>Write <paramref name="content"/> to <paramref name="path"/> atomically (temp file + rename).</summary>
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Temp beside the target so the rename stays on the same volume (a cross-volume Move is a
        // copy+delete and loses atomicity). Move(overwrite) maps to MoveFileEx REPLACE_EXISTING on
        // Windows and rename(2) on POSIX — both atomic within a volume.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Best-effort copy of <paramref name="path"/> to "&lt;path&gt;.bak" — used to preserve a file that
    /// failed to parse before it would otherwise be discarded/overwritten with defaults. Never throws:
    /// a failed backup must not mask the original problem.
    /// </summary>
    public static void Backup(string path)
    {
        try { if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true); }
        catch { /* best effort */ }
    }
}

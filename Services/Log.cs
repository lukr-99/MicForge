using System;
using System.IO;

namespace MicForge;

/// <summary>
/// Tiny append-only logger. Writes to %AppData%\MicForge\logs\micforge.log so diagnosing
/// a crash or a device problem is a matter of opening one file.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string _path;

    public static string FilePath
    {
        get
        {
            if (_path != null) return _path;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge", "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            _path = Path.Combine(dir, "micforge.log");
            return _path;
        }
    }

    public static string Folder => Path.GetDirectoryName(FilePath);

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Error(string msg, Exception ex) =>
        Write("ERROR", msg + " :: " + ex);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                // Keep the file from growing without bound.
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000)
                    File.WriteAllText(FilePath, "");
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }
}

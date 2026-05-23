using System;
using System.Diagnostics;
using System.IO;
using SignalLock.Services;

namespace SignalLock;

/// <summary>
/// Tiny dependency-free logger. All in-app logging goes through here so the
/// user (or support) can filter <c>%APPDATA%\SignalLock\signallock.log</c>
/// by category.
/// </summary>
public interface ICategoryLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Debug(string message);
}

public static class Log
{
    public static ICategoryLogger App { get; } = new CategoryLogger("app");
    public static ICategoryLogger Lock { get; } = new CategoryLogger("lock");
    public static ICategoryLogger Proximity { get; } = new CategoryLogger("proximity");
    public static ICategoryLogger Bluetooth { get; } = new CategoryLogger("bluetooth");
}

internal static class Logging
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static string LogFilePath => Path.Combine(StoragePaths.AppDataDir, "signallock.log");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(StoragePaths.AppDataDir);
            // Append mode; rolled by the user (or by deleting the file).
            var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }
        catch
        {
            // Logging must never crash the app. Falls back to Debug+Console only.
            _writer = null;
        }
    }

    internal static void Write(string level, string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] [SignalLock][{category}] {message}";
        lock (Gate)
        {
            try { _writer?.WriteLine(line); } catch { /* swallow */ }
            try { Trace.WriteLine(line); } catch { /* swallow */ }
            try { Console.WriteLine(line); } catch { /* swallow */ }
        }
    }
}

internal sealed class CategoryLogger : ICategoryLogger
{
    private readonly string _category;
    public CategoryLogger(string category) { _category = category; }
    public void Info(string message) => Logging.Write("INFO", _category, message);
    public void Warn(string message) => Logging.Write("WARN", _category, message);
    public void Error(string message) => Logging.Write("ERROR", _category, message);
    public void Debug(string message) => Logging.Write("DEBUG", _category, message);
}

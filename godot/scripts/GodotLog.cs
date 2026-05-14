using Godot;
using System;
using System.IO;

namespace LoopolisGodot;

/// <summary>
/// Lightweight logger that writes to both the Godot Output panel (GD.Print)
/// and to /tmp/loopolis-godot.log so you can inspect the full session log
/// after a crash or unexpected behaviour.
///
/// Usage:
///   GodotLog.Info("[tick] pop=42 balance=$3,200");
///   GodotLog.Warn("[zone] placement failed at (5,12): water tile");
///   GodotLog.Err("[render] null grid — skipping refresh");
/// </summary>
public static class GodotLog
{
    private const string LogPath = "/tmp/loopolis-godot.log";

    // Cleared once when Godot starts, then appended for the whole session.
    private static bool _initialized = false;

    private static void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            File.WriteAllText(LogPath, $"=== Loopolis Godot log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
        }
        catch { /* non-critical */ }
    }

    private static void Write(string prefix, string message)
    {
        EnsureInit();
        var line = $"[{DateTime.Now:HH:mm:ss}] {prefix}{message}";
        try { File.AppendAllText(LogPath, line + "\n"); } catch { }

        // Mirror to Godot Output panel
        switch (prefix)
        {
            case "WARN  ": GD.PushWarning(message); break;
            case "ERROR ": GD.PrintErr(message);    break;
            default:       GD.Print(message);       break;
        }
    }

    /// <summary>Informational log line. Appears in Output panel and log file.</summary>
    public static void Info(string message)  => Write("INFO  ", message);

    /// <summary>Warning log line. Appears as a yellow warning in the Godot editor.</summary>
    public static void Warn(string message)  => Write("WARN  ", message);

    /// <summary>Error log line. Appears in red in the Godot editor Output panel.</summary>
    public static void Err(string message)   => Write("ERROR ", message);
}

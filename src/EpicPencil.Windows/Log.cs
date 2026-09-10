// Log em arquivo (diagnóstico de campo): %LocalAppData%/EpicPencil/Logs/yyyyMMdd.log
// Regras: só eventos discretos (commit, modo, erros) — NUNCA por PointerMove
// (I/O por move mataria a latência). Thread-safe via lock; append atômico.

namespace EpicPencil.Windows;

public static class Log
{
    private static readonly object Gate = new();
    private static string _path = "";

    public static string Path
    {
        get { lock (Gate) return _path; }
    }

    public static void Init(string appName = "EpicPencil")
    {
        lock (Gate)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName, "Logs");
            System.IO.Directory.CreateDirectory(dir);
            _path = System.IO.Path.Combine(dir, $"{DateTime.Now:yyyyMMdd}.log");
            Append($"==== session start {DateTime.Now:HH:mm:ss} v{AppVersion()} os={Environment.OSVersion} ====");
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (_path == "") return; // pré-Init (ex.: testes do Core): silencioso
            Append($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
        }
    }

    private static void Append(string line)
    {
        try { System.IO.File.AppendAllText(_path, line + Environment.NewLine); }
        catch { /* log nunca pode derrubar o app */ }
    }

    private static string AppVersion() =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
}

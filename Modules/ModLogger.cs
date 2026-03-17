
// Written by:
// 
// ███╗   ██╗ ██████╗  ██████╗██╗  ██╗ █████╗ ██╗      █████╗ 
// ████╗  ██║██╔═══██╗██╔════╝██║  ██║██╔══██╗██║     ██╔══██╗
// ██╔██╗ ██║██║   ██║██║     ███████║███████║██║     ███████║
// ██║╚██╗██║██║   ██║██║     ██╔══██║██╔══██║██║     ██╔══██║
// ██║ ╚████║╚██████╔╝╚██████╗██║  ██║██║  ██║███████╗██║  ██║
// ╚═╝  ╚═══╝ ╚═════╝  ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝
//
//          ░░▒▒▓▓ https://github.com/Nochala ▓▓▒▒░░

using System;
using System.Collections.Generic;
using System.IO;

namespace EngineStateManager
{
    internal static class ModLogger
    {
        private static readonly object _writeLock = new object();
        private static readonly object _throttleLock = new object();
        private static readonly Dictionary<string, int> _lastLogTickByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        private static string _logPath = "";
        private static bool _enabled;
        private static bool _sessionInitialized;

        public static string SessionId { get; private set; } = "";
        public static bool Enabled => _enabled;
        public static string LogPath => _logPath ?? "";

        public static void Init(bool enabled, string preferredPath, string fallbackPath)
        {
            _enabled = false;

            if (!enabled)
            {
                _logPath = "";
                return;
            }

            string resolvedPath = ResolvePath(preferredPath, fallbackPath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                _logPath = "";
                return;
            }

            bool firstInitThisSession = !_sessionInitialized;
            bool pathChanged = !string.Equals(_logPath, resolvedPath, StringComparison.OrdinalIgnoreCase);

            try
            {
                string dir = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                if (firstInitThisSession)
                {
                    File.WriteAllText(resolvedPath, string.Empty);
                    SessionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    lock (_throttleLock)
                        _lastLogTickByKey.Clear();
                }
                else if (!File.Exists(resolvedPath))
                {
                    File.WriteAllText(resolvedPath, string.Empty);
                }

                _logPath = resolvedPath;
                _enabled = true;

                if (firstInitThisSession)
                {
                    _sessionInitialized = true;
                    Info("=== EngineStateManager Log Started ===");
                    Info("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    Info("SessionId: " + SessionId);
                    Info("LogPath: " + _logPath);
                }
                else if (pathChanged)
                {
                    Info("=== Logger Path Changed ===");
                    Info("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    Info("SessionId: " + SessionId);
                    Info("LogPath: " + _logPath);
                }
            }
            catch
            {
                _enabled = false;
                _logPath = "";
            }
        }

        private static string ResolvePath(string preferredPath, string fallbackPath)
        {
            if (TryPreparePath(preferredPath, out string preferredResolved))
                return preferredResolved;

            if (TryPreparePath(fallbackPath, out string fallbackResolved))
                return fallbackResolved;

            return "";
        }

        private static bool TryPreparePath(string path, out string resolvedPath)
        {
            resolvedPath = "";

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;

                string fullPath = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                resolvedPath = fullPath;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void InfoThrottled(string key, string message, int minIntervalMs)
        {
            if (!_enabled) return;
            if (string.IsNullOrEmpty(key))
            {
                Info(message);
                return;
            }

            int now = Environment.TickCount;
            bool allow = false;

            lock (_throttleLock)
            {
                if (!_lastLogTickByKey.TryGetValue(key, out int last) || unchecked(now - last) >= minIntervalMs)
                {
                    _lastLogTickByKey[key] = now;
                    allow = true;
                }
            }

            if (allow)
                Info(message);
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception ex = null)
        {
            if (ex != null)
                message = message + Environment.NewLine + ex;

            Write("ERROR", message);
        }

        public static void PersistedVehicle(
            string tag,
            int vehicleHandle,
            string reason,
            float? distFeet = null,
            float? maxFeet = null,
            int? count = null,
            int? maxCount = null)
        {
            if (!_enabled) return;

            string msg = $"[{tag}] veh={vehicleHandle} reason={reason}";
            if (distFeet.HasValue) msg += $" distFt={distFeet.Value:0}";
            if (maxFeet.HasValue) msg += $" maxFt={maxFeet.Value:0}";
            if (count.HasValue) msg += $" count={count.Value}";
            if (maxCount.HasValue) msg += $" maxCount={maxCount.Value}";
            Info(msg);
        }

        private static void Write(string level, string message)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_logPath))
                return;

            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] [{SessionId}] {level}: {message}";
                lock (_writeLock)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never allow logging to crash the game.
            }
        }
    }
}

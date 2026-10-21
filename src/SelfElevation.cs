using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PushToTalk
{
    // The same executable is both the ordinary startup entry and the elevated app.
    // The entry point exits after requesting consent; only an administrator token
    // is allowed to create the microphone control UI.
    internal static class SelfElevation
    {
        internal const string RequestedArgument = "--elevation-requested";

        internal static bool WasElevationRequested(string[] args)
        {
            if (args != null)
                foreach (string argument in args)
                    if (String.Equals(argument, RequestedArgument, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static ProcessStartInfo CreateStartInfo(string executablePath, bool startupLaunch)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("Application path is required.", "executablePath");
            string fullPath = Path.GetFullPath(executablePath);
            return new ProcessStartInfo
            {
                FileName = fullPath,
                Arguments = startupLaunch ? "--startup " + RequestedArgument : RequestedArgument,
                WorkingDirectory = Path.GetDirectoryName(fullPath),
                UseShellExecute = true,
                Verb = "runas"
            };
        }

        // Isolate the OS boundary so tests do not launch a process or request UAC.
        internal static bool Launch(string executablePath, bool startupLaunch,
            Func<string, bool> fileExists, Action<ProcessStartInfo> start)
        {
            if (fileExists == null) throw new ArgumentNullException("fileExists");
            if (start == null) throw new ArgumentNullException("start");
            ProcessStartInfo info = CreateStartInfo(executablePath, startupLaunch);
            if (!fileExists(info.FileName))
                throw new FileNotFoundException(Localization.Text(
                    "找不到 Push To Talk 主程序。", "The Push To Talk application could not be found."), info.FileName);
            try { start(info); return true; }
            catch (Win32Exception error)
            {
                // ERROR_CANCELLED: the user's decision ends startup, with no retry
                // and no ordinary-permission fallback into the main application.
                if (error.NativeErrorCode == 1223) return false;
                throw;
            }
        }
    }
}

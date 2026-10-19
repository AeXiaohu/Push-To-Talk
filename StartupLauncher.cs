using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace PushToTalk
{
    // This small, non-elevated companion is the only executable in the Run key.
    // It asks Windows for consent and never runs microphone control itself.
    internal static class StartupLauncher
    {
        [STAThread]
        private static void Main()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            Localization.SetLanguage(ReadLanguage(Path.Combine(directory, "settings.xml")));
            try
            {
                Launch(directory, File.Exists, delegate(ProcessStartInfo info)
                {
                    using (Process process = Process.Start(info)) { }
                });
            }
            catch (Exception error)
            {
                MessageBox.Show(Localization.Text("无法启动 Push To Talk：", "Unable to start Push To Talk: ") + error.Message,
                    "Push To Talk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static ProcessStartInfo CreateStartInfo(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Application directory is required.", "directory");
            string fullDirectory = Path.GetFullPath(directory);
            return new ProcessStartInfo
            {
                FileName = Path.Combine(fullDirectory, "Push To Talk.exe"),
                Arguments = "--startup",
                WorkingDirectory = fullDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };
        }

        // Inject only the OS boundary so tests never launch a process or a UAC dialog.
        internal static bool Launch(string directory, Func<string, bool> fileExists, Action<ProcessStartInfo> start)
        {
            if (fileExists == null) throw new ArgumentNullException("fileExists");
            if (start == null) throw new ArgumentNullException("start");
            ProcessStartInfo info = CreateStartInfo(directory);
            if (!fileExists(info.FileName))
                throw new FileNotFoundException(Localization.Text(
                    "请将 Push To Talk.exe 与开机启动器放在同一文件夹。",
                    "Keep Push To Talk.exe and its startup launcher in the same folder."), info.FileName);
            try { start(info); return true; }
            catch (Win32Exception error)
            {
                // ERROR_CANCELLED: respect the user's UAC decision; no retry or fallback.
                if (error.NativeErrorCode == 1223) return false;
                throw;
            }
        }

        internal static string ReadLanguage(string path)
        {
            if (!File.Exists(path)) return "system";
            try
            {
                XmlReaderSettings options = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 262144
                };
                using (XmlReader reader = XmlReader.Create(path, options))
                {
                    if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "AppSettings") return "system";
                    while (reader.Read())
                        if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.Name == "Language")
                            return Localization.NormalizeLanguage(reader.ReadElementContentAsString());
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }
            return "system";
        }
    }
}

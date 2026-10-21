using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace PushToTalk
{
    public sealed class AppSettings
    {
        public string MicrophoneId = "";
        public string Language = "system";
        public ControlMode Mode = ControlMode.PushToTalk;
        public bool EnableSounds = false;
        public bool EnableOverlay = false;
        public int OverlayX = int.MinValue;
        public int OverlayY = int.MinValue;
        public int OverlaySize = OverlayLayout.DefaultSize;
        public int OverlayOpacity = OverlayLayout.DefaultOpacity;
        public List<string> Bindings = new List<string>();

        public static AppSettings Load(string path, out string error)
        {
            error = null;
            if (!File.Exists(path)) return new AppSettings();
            try
            {
                XmlReaderSettings options = new XmlReaderSettings();
                options.DtdProcessing = DtdProcessing.Prohibit;
                options.XmlResolver = null;
                options.MaxCharactersInDocument = 262144;
                using (XmlReader reader = XmlReader.Create(path, options))
                {
                    AppSettings settings = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(reader);
                    settings.Language = Localization.NormalizeLanguage(settings.Language);
                    if (settings.Bindings == null) settings.Bindings = new List<string>();
                    if (!Enum.IsDefined(typeof(ControlMode), settings.Mode)) settings.Mode = ControlMode.PushToTalk;
                    settings.OverlaySize = OverlayLayout.NormalizeSize(settings.OverlaySize);
                    settings.OverlayOpacity = OverlayLayout.NormalizeOpacity(settings.OverlayOpacity);
                    return settings;
                }
            }
            catch (Exception)
            {
                error = Localization.Text("设置文件无法读取，已使用空白设置。", "The settings file could not be read. Default settings are being used.");
                return new AppSettings();
            }
        }

        public void Save(string path)
        {
            string temporary = path + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    new XmlSerializer(typeof(AppSettings)).Serialize(writer, this);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

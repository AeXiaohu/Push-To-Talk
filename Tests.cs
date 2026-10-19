using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PushToTalk
{
    /// <summary>Deterministic tests: no microphone changes or native key reads.</summary>
    internal static class Tests
    {
        private static int assertions;

        public static int Main()
        {
            try
            {
                TestEmptyAndOverlappingBindings();
                TestChordsAndModifiers();
                TestParsingCaptureAndImmutability();
                TestPollingPlan();
                TestPushToTalkAndReleaseGate();
                TestPushToMute();
                TestToggleEdges();
                TestLocalization();
                TestLanguageSettingsCompatibility();
                TestSettingsCompatibility();
                TestOverlaySettingsCompatibility();
                TestOverlayOpacity();
                TestOverlayLayout();
                TestStartupRegistration();
                TestStartupLauncher();
                TestSoundFeedbackTransitions();
                TestSoundFeedbackSwitchAndDisposal();
                Console.WriteLine("PASS: " + assertions + " deterministic assertions.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.Message);
                return 1;
            }
        }

        private static void TestEmptyAndOverlappingBindings()
        {
            HotkeyBinding[] none = new HotkeyBinding[0];
            Check(!HotkeyInput.IsAnyBindingDown(none, Down(Keys.F2)), "Empty list must never activate.");
            Check(!HotkeyInput.IsAnyBindingDown(null, Down(Keys.F2)), "Null list must never activate.");
            HotkeyBinding[] bindings = Bindings("F2", "MouseX1");
            Check(!Active(bindings), "No held key must not activate.");
            Check(Active(bindings, Keys.F2), "First shortcut must activate.");
            Check(Active(bindings, Keys.XButton1), "Alternative shortcut must activate.");
            Check(Active(bindings, Keys.F2, Keys.XButton1), "Both shortcuts must activate.");
            Check(Active(bindings, Keys.XButton1), "Releasing F2 while mouse held must keep activation.");
            Check(!Active(bindings), "Releasing the last shortcut must end activation.");
        }

        private static void TestChordsAndModifiers()
        {
            HotkeyBinding chord = HotkeyBinding.Parse("Ctrl+Shift+F2");
            Check(!chord.IsDown(Down(Keys.LControlKey, Keys.F2)), "Partial chord must not activate.");
            Check(chord.IsDown(Down(Keys.RControlKey, Keys.LShiftKey, Keys.F2)), "Generic modifiers must accept either side.");
            Check(chord.IsDown(Down(Keys.RControlKey, Keys.LShiftKey, Keys.F2, Keys.A)), "Unrelated extra held keys must be tolerated.");
            Check(!HotkeyBinding.Parse("LCtrl").IsDown(Down(Keys.RControlKey)), "Left Ctrl must not match right Ctrl.");
            Check(HotkeyBinding.Parse("RCtrl").IsDown(Down(Keys.RControlKey)), "Right Ctrl must match right Ctrl.");
            Check(!HotkeyBinding.Parse("RAlt").IsDown(Down(Keys.LMenu)), "Right Alt must not match left Alt.");
            Check(HotkeyBinding.Parse("Alt").IsDown(Down(Keys.RMenu)), "Generic Alt must accept right Alt.");
            Check(!HotkeyBinding.Parse("LShift").IsDown(Down(Keys.RShiftKey)), "Left Shift must not match right Shift.");
            Check(HotkeyBinding.Parse("Shift").IsDown(Down(Keys.RShiftKey)), "Generic Shift must accept right Shift.");
            Check(!HotkeyBinding.Parse("LCtrl+RCtrl").IsDown(Down(Keys.RControlKey)), "Both-side chord must require both sides.");
            Check(HotkeyBinding.Parse("LCtrl+RCtrl").IsDown(Down(Keys.LControlKey, Keys.RControlKey)), "Both-side chord must activate when both are held.");
            Check(!HotkeyBinding.Parse("MouseMiddle+F2").IsDown(Down(Keys.MButton)), "Mouse/keyboard chord must require keyboard part.");
            Check(HotkeyBinding.Parse("MouseMiddle+F2").IsDown(Down(Keys.MButton, Keys.F2)), "Mouse/keyboard chord must activate.");
        }

        private static void TestParsingCaptureAndImmutability()
        {
            HotkeyBinding original = HotkeyBinding.Parse("F2+Ctrl+Shift+Ctrl");
            Check(original.Serialize() == "Ctrl+Shift+F2", "Serialization must deduplicate and order modifiers.");
            Check(HotkeyBinding.Parse(original.Serialize()).Serialize() == original.Serialize(), "Serialized shortcut must round-trip.");
            Check(HotkeyBinding.Parse("ctrl+f2").Serialize() == "Ctrl+F2", "Parsing must be case-insensitive.");
            Check(HotkeyBinding.Parse("ControlKey+XButton2").Serialize() == "Ctrl+MouseX2", "Framework aliases must normalize.");
            Check(HotkeyBinding.Parse("Ctrl+LCtrl+F2").Serialize() == "LCtrl+F2", "Redundant generic modifier must disappear.");
            HotkeyBinding keyData = new HotkeyBinding(new Keys[] { Keys.Control | Keys.Shift | Keys.F2 });
            Check(keyData.Serialize() == "Ctrl+Shift+F2", "WinForms modifier flags must expand into a chord.");
            Keys[] copy = original.VirtualKeys;
            copy[0] = Keys.A;
            Check(original.Serialize() == "Ctrl+Shift+F2", "Exposed key array must not mutate the binding.");
            Check(HotkeyInput.CaptureBinding(Down(Keys.LButton, Keys.RButton)) == null, "UI mouse clicks must not be captured.");
            Check(HotkeyInput.CaptureBinding(Down()) == null, "Empty capture must return null.");
            Check(HotkeyInput.CaptureBinding(Down(Keys.ControlKey, Keys.RControlKey, Keys.XButton2)).Serialize() == "RCtrl+MouseX2", "Capture must preserve side and remove generic duplicate.");
            Check(HotkeyInput.CaptureBinding(Down(Keys.MButton)).Serialize() == "MouseMiddle", "Middle mouse must be capturable.");
            Check(!HotkeyInput.IsBindableKey(Keys.LButton), "Left mouse must be excluded.");
            Check(!HotkeyInput.IsBindableKey((Keys)255), "Unrecognized virtual key must be excluded.");
            foreach (string invalid in new string[] { "", " ", "Ctrl++F2", "Ctrl+NoSuchKey", "LButton", "None", "+F2", "F2+" })
            {
                HotkeyBinding binding;
                string message;
                Check(!HotkeyBinding.TryParse(invalid, out binding, out message) && binding == null && !String.IsNullOrWhiteSpace(message), "Invalid shortcut must fail safely: " + invalid);
            }
            foreach (Keys key in Enum.GetValues(typeof(Keys)))
            {
                if (!HotkeyInput.IsBindableKey(key)) continue;
                HotkeyBinding binding = new HotkeyBinding(new Keys[] { key });
                Check(HotkeyBinding.Parse(binding.Serialize()).Serialize() == binding.Serialize(), "Every bindable key must round-trip: " + key);
            }
        }

        private static void TestPollingPlan()
        {
            Check(HotkeyInput.CreatePoller(new HotkeyBinding[0]).PolledKeyCount == 0, "No bindings must poll zero keys.");
            Check(HotkeyInput.CreatePoller(Bindings("F2", "F2")).PolledKeyCount == 1, "Polling plan must deduplicate keys across bindings.");
            Check(HotkeyInput.CreatePoller(Bindings("Ctrl+Shift+MouseX1", "LCtrl+F2")).PolledKeyCount == 6, "Generic modifiers must expand to physical sides and deduplicate.");
            Check(HotkeyInput.CreatePoller(Bindings("LCtrl+F2")).PolledKeyCount == 2, "Side-specific modifiers must not add the other side.");
        }

        private static void TestPushToTalkAndReleaseGate()
        {
            MuteStateMachine empty = new MuteStateMachine(ControlMode.PushToTalk, Bindings(), false);
            Check(empty.Update(Down()), "Empty push-to-talk must remain muted.");
            Check(empty.Update(Down(Keys.F2)), "Unbound key must not open empty push-to-talk.");
            MuteStateMachine state = new MuteStateMachine(ControlMode.PushToTalk, Bindings("F2", "MouseX1"), false);
            Check(state.Update(Down(Keys.F2)), "A key held before enabling must stay muted.");
            Check(state.WaitingForRelease, "Held initial key must retain release gate.");
            Check(state.Update(Down(Keys.F2, Keys.XButton1)), "Adding another held binding must not bypass initial gate.");
            Check(state.Update(Down(Keys.XButton1)), "Initial gate must wait for all matching bindings to release.");
            Check(state.Update(Down()), "Initial release must remain muted.");
            Check(!state.WaitingForRelease, "Initial release must arm push-to-talk.");
            Check(!state.Update(Down(Keys.F2)), "Fresh press must unmute.");
            Check(!state.Update(Down(Keys.F2, Keys.XButton1)), "Overlapping shortcuts must stay unmuted.");
            Check(!state.Update(Down(Keys.XButton1)), "Release of one shortcut must not mute the other.");
            Check(state.Update(Down()), "Final release must mute.");
            state.RequireRelease();
            Check(state.Update(Down(Keys.F2)), "Rearming must require release again.");
            Check(state.Update(Down()), "Rearming release must remain muted.");
            Check(!state.Update(Down(Keys.XButton1)), "Fresh press after rearming must unmute.");
        }

        private static void TestPushToMute()
        {
            MuteStateMachine state = new MuteStateMachine(ControlMode.PushToMute, Bindings("F2", "MouseX1"), false);
            Check(state.Update(Down()), "Push-to-mute must initially pass through the muted release gate.");
            Check(!state.Update(Down()), "Armed push-to-mute must be open while no shortcut held.");
            Check(state.Update(Down(Keys.F2)), "Push-to-mute press must mute.");
            Check(state.Update(Down(Keys.F2, Keys.XButton1)), "Overlapping push-to-mute bindings must stay muted.");
            Check(state.Update(Down(Keys.XButton1)), "Releasing one push-to-mute binding must keep the other muted.");
            Check(!state.Update(Down()), "Releasing last push-to-mute binding must unmute.");
        }

        private static void TestToggleEdges()
        {
            MuteStateMachine state = new MuteStateMachine(ControlMode.Toggle, Bindings("F2", "MouseX1"), true);
            Check(state.Update(Down()), "Toggle must pass through initial muted release gate.");
            Check(state.Update(Down()), "Toggle must preserve original mute before a press.");
            Check(!state.Update(Down(Keys.F2)), "First rising edge must unmute.");
            Check(!state.Update(Down(Keys.F2)), "Holding a key must not repeat toggles.");
            Check(!state.Update(Down(Keys.F2, Keys.XButton1)), "A second binding joining an existing hold must not toggle again.");
            Check(!state.Update(Down(Keys.F2, Keys.XButton1)), "Holding both must not repeat toggles.");
            Check(!state.Update(Down(Keys.XButton1)), "Releasing one overlapping hold must not toggle.");
            Check(!state.Update(Down()), "All released must preserve toggle state.");
            Check(state.Update(Down(Keys.XButton1)), "A fresh press after all bindings release must create a new rising edge.");
            Check(state.Update(Down()), "Release after toggle must preserve state.");

            MuteStateMachine simultaneous = new MuteStateMachine(ControlMode.Toggle, Bindings("F2", "MouseX1"), true);
            simultaneous.Update(Down());
            Check(!simultaneous.Update(Down(Keys.F2, Keys.XButton1)), "Simultaneous rising edges must toggle once per sample.");
            MuteStateMachine initial = new MuteStateMachine(ControlMode.Toggle, Bindings("F2"), true);
            Check(initial.Update(Down(Keys.F2)), "Initial held key must not toggle.");
            Check(initial.Update(Down()), "Releasing initial held key must not toggle.");
            Check(!initial.Update(Down(Keys.F2)), "Fresh key after initial release must toggle.");

            MuteStateMachine nested = new MuteStateMachine(ControlMode.Toggle, Bindings("F2", "Ctrl+F2"), true);
            nested.Update(Down());
            Check(!nested.Update(Down(Keys.F2)), "Base key of a nested shortcut must toggle once.");
            Check(!nested.Update(Down(Keys.F2, Keys.LControlKey)), "Adding Ctrl to a held F2 must not toggle twice.");
            Check(!nested.Update(Down(Keys.F2)), "Releasing Ctrl while F2 remains held must not toggle.");
            Check(!nested.Update(Down(Keys.F2, Keys.RControlKey)), "Changing modifier side during the same hold must not toggle.");
            Check(!nested.Update(Down()), "Releasing nested shortcut must preserve toggle state.");
            Check(nested.Update(Down(Keys.F2, Keys.RControlKey)), "Fresh nested chord after full release must toggle once.");
        }

        private static void TestLocalization()
        {
            string originalChoice = Localization.Choice;
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                Check(Localization.Choice == "system", "The first launch must follow the current Windows display language.");
                foreach (string culture in new string[] { "zh-CN", "zh-TW", "zh-HK", "zh-SG", "zh" })
                    Check(Localization.ResolveIsChinese("system", CultureInfo.GetCultureInfo(culture)),
                        "Chinese Windows display languages must select Chinese text: " + culture);
                foreach (string culture in new string[] { "en-US", "en-GB", "ja-JP", "fr-FR", "" })
                    Check(!Localization.ResolveIsChinese("system", CultureInfo.GetCultureInfo(culture)),
                        "Other Windows display languages must fall back to English text: " + culture);
                Check(!Localization.ResolveIsChinese("system", null), "Unavailable system language information must safely fall back to English.");
                Check(Localization.ResolveIsChinese("zh-CN", CultureInfo.GetCultureInfo("en-US")),
                    "An explicit Chinese choice must override an English system language.");
                Check(!Localization.ResolveIsChinese("en-US", CultureInfo.GetCultureInfo("zh-CN")),
                    "An explicit English choice must override a Chinese system language.");
                foreach (string invalid in new string[] { null, "", " ", "invalid", "fr-FR" })
                    Check(Localization.NormalizeLanguage(invalid) == "system", "Unknown language choices must return to the system default.");
                Check(Localization.NormalizeLanguage(" SYSTEM ") == "system", "The system choice must normalize whitespace and case.");
                Check(Localization.NormalizeLanguage(" ZH-cn ") == "zh-CN", "Chinese language choices must normalize whitespace and case.");
                Check(Localization.NormalizeLanguage(" En-uS ") == "en-US", "English language choices must normalize whitespace and case.");

                Localization.SetLanguage("zh-CN");
                Check(Localization.Choice == "zh-CN" && Localization.IsChinese && Localization.Text("中文", "English") == "中文",
                    "Switching to Chinese must immediately use the Chinese application text.");
                Localization.SetLanguage("en-US");
                Check(Localization.Choice == "en-US" && !Localization.IsChinese && Localization.Text("中文", "English") == "English",
                    "Switching to English must immediately use the English application text.");
                Localization.SetLanguage("system");
                Check(Localization.Choice == "system" && Localization.IsChinese == Localization.ResolveIsChinese("system", originalUiCulture),
                    "Returning to the system choice must resolve the current display language instead of remembering the previous explicit choice.");
                Check(Object.ReferenceEquals(originalCulture, CultureInfo.CurrentCulture) && Object.ReferenceEquals(originalUiCulture, CultureInfo.CurrentUICulture),
                    "Application language switching must never change process thread culture or Windows preferences.");
            }
            finally { Localization.SetLanguage(originalChoice); }
        }

        private static void TestLanguageSettingsCompatibility()
        {
            string originalChoice = Localization.Choice;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "language-settings-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Check(new AppSettings().Language == "system", "Fresh settings must follow the system display language.");
                string error;
                AppSettings absent = AppSettings.Load(path, out error);
                Check(error == null && absent.Language == "system", "A missing settings file must follow the system display language.");
                File.WriteAllText(path, "<AppSettings><MicrophoneId>language-test-microphone</MicrophoneId><Mode>PushToTalk</Mode><EnableSounds>false</EnableSounds><EnableOverlay>false</EnableOverlay><OverlayX>2268</OverlayX><OverlayY>611</OverlayY><OverlaySize>48</OverlaySize><OverlayOpacity>50</OverlayOpacity><Bindings><string>V</string></Bindings></AppSettings>", new UTF8Encoding(false));
                AppSettings legacy = AppSettings.Load(path, out error);
                Check(error == null && legacy.Language == "system", "Existing settings without a language field must acquire the system language default.");
                CheckLanguagePreferences(legacy, "loading an existing configuration without a language");
                foreach (string language in new string[] { "zh-CN", "en-US", "system" })
                {
                    legacy.Language = language;
                    legacy.Save(path);
                    AppSettings saved = AppSettings.Load(path, out error);
                    Check(error == null && saved.Language == language, "Every supported language preference must round-trip: " + language);
                    CheckLanguagePreferences(saved, "saving language " + language);
                }
                legacy.Language = "invalid";
                legacy.Save(path);
                AppSettings repaired = AppSettings.Load(path, out error);
                Check(error == null && repaired.Language == "system", "An invalid saved language must recover to the system default without discarding the configuration.");
                CheckLanguagePreferences(repaired, "repairing an invalid saved language");
                Check(!File.Exists(path + ".tmp"), "Language preference saves must leave no temporary file.");

                File.WriteAllText(path, "invalid xml", new UTF8Encoding(false));
                Localization.SetLanguage("en-US");
                AppSettings fallback = AppSettings.Load(path, out error);
                Check(fallback.Language == "system" && error == "The settings file could not be read. Default settings are being used.",
                    "Unreadable settings must report an English error when English is selected.");
                Localization.SetLanguage("zh-CN");
                fallback = AppSettings.Load(path, out error);
                Check(fallback.Language == "system" && error == "设置文件无法读取，已使用空白设置。",
                    "Unreadable settings must report a Chinese error when Chinese is selected.");
            }
            finally
            {
                Localization.SetLanguage(originalChoice);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        private static void CheckLanguagePreferences(AppSettings settings, string operation)
        {
            Check(settings.MicrophoneId == "language-test-microphone" && settings.Mode == ControlMode.PushToTalk && !settings.EnableSounds &&
                settings.Bindings.Count == 1 && settings.Bindings[0] == "V",
                "Existing microphone, control mode, disabled sounds, and V binding must survive " + operation + ".");
            Check(!settings.EnableOverlay && settings.OverlayX == 2268 && settings.OverlayY == 611 && settings.OverlaySize == 48 && settings.OverlayOpacity == 50,
                "The existing disabled overlay and its edited geometry and opacity must survive " + operation + ".");
        }

        private static void TestSettingsCompatibility()
        {
            AppSettings defaults = new AppSettings();
            Check(!defaults.EnableSounds, "Fresh settings must disable sounds.");
            Check(defaults.Bindings.Count == 0, "Fresh settings must not bind any keys.");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-test-" + Guid.NewGuid().ToString("N") + ".xml");
            const string microphoneId = "{0.0.1.00000000}.{00000000-1111-2222-3333-444444444444}";
            try
            {
                string error;
                AppSettings absent = AppSettings.Load(path, out error);
                Check(error == null && !absent.EnableSounds && absent.Bindings.Count == 0, "Missing config must retain silent, unbound defaults.");
                // Pre-sound releases used this root and contained no EnableSounds element.
                File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><AppSettings><MicrophoneId>" + microphoneId + "</MicrophoneId><Mode>PushToMute</Mode><Bindings><string>F2</string></Bindings></AppSettings>", new UTF8Encoding(false));
                AppSettings legacy = AppSettings.Load(path, out error);
                Check(error == null, "Legacy settings must load without migration errors.");
                Check(!legacy.EnableSounds, "Legacy settings must keep new sounds disabled.");
                Check(legacy.MicrophoneId == microphoneId, "Legacy microphone choice must survive the upgrade.");
                Check(legacy.Mode == ControlMode.PushToMute, "Legacy control mode must survive the upgrade.");
                Check(legacy.Bindings.Count == 1 && legacy.Bindings[0] == "F2", "Legacy F2 binding must survive the upgrade.");

                legacy.EnableSounds = true;
                legacy.Bindings.Add("MouseX1");
                legacy.Save(path);
                AppSettings enabled = AppSettings.Load(path, out error);
                Check(error == null && enabled.EnableSounds, "Enabled sound preference must round-trip.");
                Check(enabled.MicrophoneId == microphoneId && enabled.Mode == ControlMode.PushToMute, "Saving sounds must preserve microphone and mode.");
                Check(enabled.Bindings.Count == 2 && enabled.Bindings[0] == "F2" && enabled.Bindings[1] == "MouseX1", "Saving sounds must preserve every shortcut.");

                enabled.EnableSounds = false;
                enabled.Save(path);
                AppSettings disabled = AppSettings.Load(path, out error);
                Check(error == null && !disabled.EnableSounds, "Disabled sound preference must round-trip through file replacement.");
                Check(disabled.MicrophoneId == microphoneId && disabled.Mode == ControlMode.PushToMute && disabled.Bindings.Count == 2, "Disabling sounds must preserve existing configuration.");
                Check(!File.Exists(path + ".tmp"), "Successful settings saves must remove their temporary file.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        private static void TestOverlaySettingsCompatibility()
        {
            AppSettings defaults = new AppSettings();
            Check(!defaults.EnableOverlay, "Fresh settings must keep the screen overlay disabled.");
            Check(defaults.OverlayX == Int32.MinValue && defaults.OverlayY == Int32.MinValue,
                "A fresh overlay must use an unpositioned sentinel instead of assuming a monitor origin.");
            Check(defaults.OverlaySize == OverlayLayout.DefaultSize, "Fresh overlay size must match its default layout size.");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay-settings-test-" + Guid.NewGuid().ToString("N") + ".xml");
            const string microphoneId = "{0.0.1.00000000}.{55555555-1111-2222-3333-444444444444}";
            try
            {
                string error;
                AppSettings absent = AppSettings.Load(path, out error);
                Check(error == null && !absent.EnableOverlay, "Missing settings must not enable a screen overlay.");
                File.WriteAllText(path, "<AppSettings><MicrophoneId>" + microphoneId + "</MicrophoneId><Mode>Toggle</Mode><EnableSounds>false</EnableSounds><Bindings><string>V</string></Bindings></AppSettings>", new UTF8Encoding(false));
                AppSettings legacy = AppSettings.Load(path, out error);
                Check(error == null && !legacy.EnableOverlay, "Existing settings without overlay fields must upgrade with the overlay disabled.");
                Check(legacy.MicrophoneId == microphoneId && legacy.Mode == ControlMode.Toggle && !legacy.EnableSounds,
                    "Adding overlay preferences must preserve an existing microphone, mode, and disabled sound preference.");
                Check(legacy.Bindings.Count == 1 && legacy.Bindings[0] == "V", "Adding overlay preferences must preserve the user's V shortcut.");
                Check(legacy.OverlaySize == OverlayLayout.DefaultSize && legacy.OverlayX == Int32.MinValue && legacy.OverlayY == Int32.MinValue,
                    "Existing settings must obtain complete default overlay geometry.");

                legacy.EnableOverlay = true;
                legacy.OverlayX = -1176;
                legacy.OverlayY = -876;
                legacy.OverlaySize = 136;
                legacy.Save(path);
                AppSettings enabled = AppSettings.Load(path, out error);
                Check(error == null && enabled.EnableOverlay, "An explicitly enabled overlay must remain enabled after restart.");
                Check(enabled.OverlayX == -1176 && enabled.OverlayY == -876 && enabled.OverlaySize == 136,
                    "Edited overlay position and size on a negative-coordinate monitor must round-trip.");
                Check(enabled.MicrophoneId == microphoneId && enabled.Mode == ControlMode.Toggle && !enabled.EnableSounds && enabled.Bindings.Count == 1 && enabled.Bindings[0] == "V",
                    "Saving overlay preferences must not alter existing input or sound preferences.");

                enabled.EnableOverlay = false;
                enabled.Save(path);
                AppSettings disabled = AppSettings.Load(path, out error);
                Check(error == null && !disabled.EnableOverlay && disabled.OverlayX == -1176 && disabled.OverlayY == -876 && disabled.OverlaySize == 136,
                    "Disabling the overlay must retain the edited geometry for the next enable.");
                disabled.OverlaySize = Int32.MinValue;
                disabled.Save(path);
                AppSettings undersized = AppSettings.Load(path, out error);
                Check(error == null && undersized.OverlaySize == OverlayLayout.MinimumSize, "Corrupt negative overlay sizes must load at the minimum.");
                disabled.OverlaySize = Int32.MaxValue;
                disabled.Save(path);
                AppSettings oversized = AppSettings.Load(path, out error);
                Check(error == null && oversized.OverlaySize == OverlayLayout.MaximumSize, "Corrupt huge overlay sizes must load at the maximum.");
                Check(oversized.MicrophoneId == microphoneId && oversized.Bindings.Count == 1 && oversized.Bindings[0] == "V" && !oversized.EnableSounds,
                    "Repairing overlay geometry must preserve unrelated user preferences.");
                Check(!File.Exists(path + ".tmp"), "Overlay preference saves must leave no temporary file.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        private static void TestOverlayOpacity()
        {
            Check(new AppSettings().OverlayOpacity == 100, "A new overlay must default to its full opacity.");
            Check(OverlayLayout.DefaultOpacity == 100 && OverlayLayout.MinimumOpacity == 20 && OverlayLayout.MaximumOpacity == 100,
                "The overlay must remain visible throughout its supported 20 to 100 percent opacity range.");
            int[] values = { Int32.MinValue, -1, 0, 19, 20, 35, 100, 101, Int32.MaxValue };
            int[] expected = { 20, 20, 20, 20, 20, 35, 100, 100, 100 };
            for (int i = 0; i < values.Length; i++)
                Check(OverlayLayout.NormalizeOpacity(values[i]) == expected[i],
                    "Opacity must preserve valid choices and safely clamp unsupported values: " + values[i]);

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay-opacity-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                string error;
                Check(AppSettings.Load(path, out error).OverlayOpacity == 100 && error == null,
                    "Missing settings must default to full opacity without reporting an error.");
                // This is a complete pre-opacity overlay configuration. Its other preferences must survive.
                File.WriteAllText(path, "<AppSettings><MicrophoneId>opacity-test-microphone</MicrophoneId><Mode>PushToMute</Mode><EnableSounds>true</EnableSounds><EnableOverlay>true</EnableOverlay><OverlayX>-999</OverlayX><OverlayY>-100</OverlayY><OverlaySize>136</OverlaySize><Bindings><string>V</string><string>Ctrl+MouseX1</string></Bindings></AppSettings>", new UTF8Encoding(false));
                AppSettings legacy = AppSettings.Load(path, out error);
                Check(error == null && legacy.OverlayOpacity == 100, "Existing overlay configurations without an opacity field must keep their previous appearance.");
                CheckOpacityPreferences(legacy, "loading a pre-opacity configuration");
                legacy.OverlayOpacity = 35;
                legacy.Save(path);
                AppSettings edited = AppSettings.Load(path, out error);
                Check(error == null && edited.OverlayOpacity == 35, "An edited overlay opacity must persist after restart.");
                CheckOpacityPreferences(edited, "saving an edited opacity");

                foreach (int invalid in new int[] { Int32.MinValue, 0, Int32.MaxValue })
                {
                    edited.OverlayOpacity = invalid;
                    edited.Save(path);
                    AppSettings repaired = AppSettings.Load(path, out error);
                    Check(error == null && repaired.OverlayOpacity == (invalid < 20 ? 20 : 100),
                        "An out-of-range saved opacity must be repaired without resetting the configuration: " + invalid);
                    CheckOpacityPreferences(repaired, "repairing saved opacity " + invalid);
                }
                edited.EnableOverlay = false;
                edited.OverlayOpacity = 35;
                edited.Save(path);
                AppSettings disabled = AppSettings.Load(path, out error);
                Check(error == null && !disabled.EnableOverlay && disabled.OverlayOpacity == 35,
                    "Disabling the overlay must retain its edited opacity for the next use.");
                Check(!File.Exists(path + ".tmp"), "Opacity preference saves must leave no temporary file.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            }
        }

        private static void CheckOpacityPreferences(AppSettings settings, string operation)
        {
            Check(settings.MicrophoneId == "opacity-test-microphone" && settings.Mode == ControlMode.PushToMute && settings.EnableSounds &&
                settings.Bindings.Count == 2 && settings.Bindings[0] == "V" && settings.Bindings[1] == "Ctrl+MouseX1" &&
                settings.EnableOverlay && settings.OverlayX == -999 && settings.OverlayY == -100 && settings.OverlaySize == 136,
                "All existing microphone, input, sound, and overlay geometry preferences must survive " + operation + ".");
        }

        private static void TestOverlayLayout()
        {
            Rectangle primary = new Rectangle(0, 0, 1920, 1040);
            Rectangle left = new Rectangle(-1280, 0, 1280, 984);
            Rectangle[] screens = new Rectangle[] { primary, left };
            Rectangle saved = new Rectangle(-1200, 100, 80, 80);
            Check(OverlayLayout.GetBounds(saved.X, saved.Y, saved.Width, screens) == saved,
                "A fully visible edited overlay on a left-hand monitor must keep its exact saved position.");
            Rectangle above = new Rectangle(-1280, -1024, 1280, 1024);
            Check(OverlayLayout.GetBounds(-1000, -900, 136, new Rectangle[] { primary, above }) == new Rectangle(-1000, -900, 136, 136),
                "A fully visible overlay on an above-left monitor must preserve both negative coordinates.");
            Check(OverlayLayout.GetBounds(100, 100, 80, screens) == new Rectangle(100, 100, 80, 80),
                "Ordinary saved geometry must be stable across launches.");
            Check(OverlayLayout.GetBounds(-30, 50, 80, screens) == new Rectangle(0, 50, 80, 80),
                "An overlay straddling monitors must move onto the monitor holding most of its area.");
            Check(OverlayLayout.GetBounds(-70, 50, 80, screens) == new Rectangle(-80, 50, 80, 80),
                "The larger-overlap rule must also select a negative-coordinate monitor.");
            Check(OverlayLayout.GetBounds(100, 1000, 80, new Rectangle[] { primary }) == new Rectangle(100, 960, 80, 80),
                "The overlay must remain inside the working area above the taskbar.");
            Check(OverlayLayout.GetBounds(2500, 100, 80, new Rectangle[] { primary }) == new Rectangle(1840, 100, 80, 80),
                "Removing the saved monitor must recover the overlay onto the remaining display.");
            Rectangle[] separated = new Rectangle[] { new Rectangle(0, 0, 1000, 800), new Rectangle(2000, 0, 1000, 800) };
            Check(OverlayLayout.GetBounds(1500, 100, 80, separated) == new Rectangle(2000, 100, 80, 80),
                "An overlay in a monitor gap must recover onto the nearer display.");

            Rectangle defaultBounds = OverlayLayout.GetBounds(Int32.MinValue, Int32.MinValue, OverlayLayout.DefaultSize, screens);
            Check(defaultBounds == new Rectangle(primary.Right - OverlayLayout.DefaultSize - OverlayLayout.DefaultMargin,
                primary.Top + OverlayLayout.DefaultMargin, OverlayLayout.DefaultSize, OverlayLayout.DefaultSize),
                "First use must choose a visible position with a small margin on the first working area.");
            Check(OverlayLayout.GetBounds(Int32.MinValue, 400, 80, screens) == defaultBounds &&
                OverlayLayout.GetBounds(400, Int32.MinValue, 80, screens) == defaultBounds,
                "Either missing coordinate must reset the whole position instead of moving the overlay off screen.");
            Rectangle negativeDefault = OverlayLayout.GetBounds(Int32.MinValue, Int32.MinValue, 80, new Rectangle[] { above, primary });
            Check(negativeDefault == new Rectangle(above.Right - 80 - OverlayLayout.DefaultMargin, above.Top + OverlayLayout.DefaultMargin, 80, 80),
                "First-use placement must honor a first monitor whose origin is negative.");

            foreach (int size in new int[] { Int32.MinValue, -1, 0, OverlayLayout.MinimumSize - 1 })
                Check(OverlayLayout.GetBounds(100, 100, size, screens) == new Rectangle(100, 100, OverlayLayout.MinimumSize, OverlayLayout.MinimumSize),
                    "Invalid or undersized overlay geometry must use the supported minimum: " + size);
            foreach (int size in new int[] { OverlayLayout.MaximumSize + 1, 65536, Int32.MaxValue })
                Check(OverlayLayout.GetBounds(100, 100, size, screens) == new Rectangle(100, 100, OverlayLayout.MaximumSize, OverlayLayout.MaximumSize),
                    "Oversized overlay geometry must use the supported maximum without overflow: " + size);
            foreach (int size in new int[] { OverlayLayout.MinimumSize, OverlayLayout.DefaultSize, 136, OverlayLayout.MaximumSize })
                Check(OverlayLayout.GetBounds(100, 100, size, screens) == new Rectangle(100, 100, size, size),
                    "A supported edited overlay size must remain unchanged: " + size);

            Rectangle tiny = new Rectangle(10, 20, 30, 20);
            Rectangle tinyBounds = OverlayLayout.GetBounds(300, 400, 256, new Rectangle[] { tiny });
            Check(tinyBounds.Width == 20 && tinyBounds.Height == 20 && Contains(tiny, tinyBounds),
                "An unusually small working area must still contain the complete overlay.");
            Rectangle fallback = OverlayLayout.GetBounds(Int32.MinValue, Int32.MinValue, 80, null);
            Check(fallback.Width == 80 && fallback.Height == 80 && Contains(new Rectangle(0, 0, 1920, 1080), fallback),
                "Missing display data must choose safe visible fallback geometry.");
            Check(OverlayLayout.GetBounds(Int32.MinValue, Int32.MinValue, 80, new Rectangle[0]) == fallback,
                "An empty display list must use the same fallback as missing display data.");
            Check(OverlayLayout.GetBounds(Int32.MinValue, Int32.MinValue, 80,
                new Rectangle[] { Rectangle.Empty, new Rectangle(0, 0, -1, 500), new Rectangle(0, 0, 500, 0) }) == fallback,
                "Invalid display areas must not produce an invisible overlay.");
            Check(OverlayLayout.GetBounds(-1200, 100, 80, new Rectangle[] { Rectangle.Empty, left }) == saved,
                "Invalid display entries must not hide a subsequent valid saved monitor.");

            foreach (Point position in new Point[] { new Point(Int32.MaxValue, Int32.MaxValue), new Point(Int32.MaxValue, Int32.MinValue + 1),
                new Point(Int32.MinValue + 1, Int32.MaxValue), new Point(Int32.MinValue + 1, Int32.MinValue + 1) })
            {
                Rectangle safe = OverlayLayout.GetBounds(position.X, position.Y, Int32.MaxValue, screens);
                Check(safe.Width == OverlayLayout.MaximumSize && safe.Height == safe.Width && (Contains(primary, safe) || Contains(left, safe)),
                    "Extreme saved coordinates and sizes must recover completely onto one monitor without integer overflow: " + position);
            }
            Rectangle farPositive = new Rectangle(Int32.MaxValue - 2000, Int32.MaxValue - 1500, 2000, 1500);
            Rectangle farNegative = new Rectangle(Int32.MinValue + 1, Int32.MinValue + 1, 2000, 1500);
            Rectangle extreme = OverlayLayout.GetBounds(0, 0, 256, new Rectangle[] { farPositive, farNegative });
            Check(Contains(farPositive, extreme) || Contains(farNegative, extreme),
                "Distance selection and clamping must tolerate monitor coordinates close to integer limits.");
        }

        private static bool Contains(Rectangle outer, Rectangle inner)
        {
            return inner.Width > 0 && inner.Height > 0 && inner.X >= outer.X && inner.Y >= outer.Y &&
                (long)inner.X + inner.Width <= (long)outer.X + outer.Width &&
                (long)inner.Y + inner.Height <= (long)outer.Y + outer.Height;
        }

        private static void TestStartupRegistration()
        {
            Check(!StartupRegistration.IsStartupLaunch(null), "Null arguments must use a normal visible launch.");
            Check(!StartupRegistration.IsStartupLaunch(new string[0]), "A manual launch without arguments must stay visible.");
            Check(!StartupRegistration.IsStartupLaunch(new string[] { "--other", "startup" }), "Unrelated arguments must not hide the main window.");
            Check(StartupRegistration.IsStartupLaunch(new string[] { "--startup" }), "The startup argument must request a tray-only launch.");
            Check(StartupRegistration.IsStartupLaunch(new string[] { "--other", "--StArTuP" }), "The startup argument must be case-insensitive and accepted after other arguments.");
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Push To Talk.exe");
            string expected = "\"" + Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)), "Push To Talk Startup.exe") + "\"";
            StartupStore store = new StartupStore();
            StartupRegistration registration = new StartupRegistration(store, delegate(string path) { return true; });
            Check(!registration.IsEnabled(executable), "A missing startup entry must default to off.");
            Check(!registration.MigrateLegacyRegistration(executable), "Opening a fresh installation must not turn startup on.");
            Check(store.Writes == 0 && store.Deletes == 0, "Reading startup state must never change registration.");
            Check(StartupRegistration.BuildCommandLine(executable) == expected, "Startup must register the quoted, non-elevated companion instead of the administrator application.");
            registration.SetEnabled(executable, true);
            Check(store.Writes == 1 && store.Deletes == 0 && (string)store.Value == expected, "Explicit enable must save the current executable command.");
            Check(registration.IsEnabled(executable), "The registered executable must be shown enabled.");
            store.Value = expected.ToUpperInvariant();
            Check(registration.IsEnabled(executable), "Windows startup executable comparison must be case-insensitive.");
            store.Value = "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "old", "Push To Talk.exe") + "\" --startup";
            Check(!registration.IsEnabled(executable), "An entry for a moved executable must not falsely enable this copy.");
            Check(store.Writes == 1 && store.Deletes == 0, "Inspecting a stale entry must not silently repair or delete it.");
            store.Value = 1;
            Check(!registration.IsEnabled(executable), "Malformed startup value types must remain disabled.");
            registration.SetEnabled(executable, false);
            Check(store.Value == null && store.Writes == 1 && store.Deletes == 1, "Explicit disable must delete only its own registration through the store.");
            Check(!registration.IsEnabled(executable), "Disabled registration must remain disabled.");
            store.FailWrite = true;
            bool failed = false;
            try { registration.SetEnabled(executable, true); }
            catch (IOException) { failed = true; }
            Check(failed && store.Value == null, "Registration failures must reach the UI without pretending to enable startup.");
            bool invalidPath = false;
            try { StartupRegistration.BuildCommandLine(" "); }
            catch (ArgumentException) { invalidPath = true; }
            Check(invalidPath, "Blank startup executable paths must be rejected.");
            bool tooLong = false;
            try { StartupRegistration.BuildCommandLine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, new string('x', 250), "Push To Talk.exe")); }
            catch (PathTooLongException) { tooLong = true; }
            Check(tooLong, "Unsupported Windows Run command lengths must fail visibly.");

            StartupStore legacyStore = new StartupStore();
            StartupRegistration legacy = new StartupRegistration(legacyStore, delegate(string path) { return true; });
            legacyStore.Value = "\"" + Path.GetFullPath(executable) + "\" --startup";
            Check(!legacy.IsEnabled(executable) && legacyStore.Writes == 0, "Reading a legacy registration alone must not migrate it or claim the new launcher is registered.");
            Check(legacy.MigrateLegacyRegistration(executable), "An explicitly enabled 1.0 registration for this installation must migrate.");
            Check((string)legacyStore.Value == expected && legacyStore.Writes == 1 && legacy.IsEnabled(executable), "Migration must preserve enabled startup using the new companion command.");
            Check(!legacy.MigrateLegacyRegistration(executable) && legacyStore.Writes == 1, "A migrated registration must not be rewritten on every launch.");
            legacyStore.Value = "\"" + Path.GetFullPath(executable).ToUpperInvariant() + "\" --STARTUP";
            Check(legacy.MigrateLegacyRegistration(executable), "Legacy command matching must follow Windows case-insensitive path semantics.");
            legacyStore.Value = "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "other installation", "Push To Talk.exe") + "\" --startup";
            int previousWrites = legacyStore.Writes;
            Check(!legacy.MigrateLegacyRegistration(executable) && legacyStore.Writes == previousWrites, "Migration must leave other installation paths untouched.");
            legacyStore.Value = 1;
            Check(!legacy.MigrateLegacyRegistration(executable) && legacyStore.Writes == previousWrites, "Malformed existing values must not enable startup.");

            StartupStore missingStore = new StartupStore();
            StartupRegistration missing = new StartupRegistration(missingStore, delegate(string path) { return false; });
            bool missingFile = false;
            try { missing.SetEnabled(executable, true); }
            catch (FileNotFoundException) { missingFile = true; }
            Check(missingFile && missingStore.Writes == 0, "A missing companion must not leave a falsely enabled startup entry.");
            missingStore.Value = expected;
            Check(!missing.IsEnabled(executable), "A startup entry whose companion is missing must not be shown as usable.");
            missing.SetEnabled(executable, false);
            Check(missingStore.Value == null && missingStore.Deletes == 1, "Disabling startup must work even when the companion has been removed.");
            string legacyValue = "\"" + Path.GetFullPath(executable) + "\" --startup";
            missingStore.Value = legacyValue;
            missingFile = false;
            try { missing.MigrateLegacyRegistration(executable); }
            catch (FileNotFoundException) { missingFile = true; }
            Check(missingFile && (string)missingStore.Value == legacyValue && missingStore.Writes == 0,
                "A failed upgrade must preserve the previous startup preference without writing a broken command.");
            StartupStore renamedStore = new StartupStore();
            StartupRegistration renamed = new StartupRegistration(renamedStore, delegate(string path) { return true; });
            bool renamedRejected = false;
            try { renamed.SetEnabled(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Renamed.exe"), true); }
            catch (InvalidOperationException) { renamedRejected = true; }
            Check(renamedRejected && renamedStore.Writes == 0, "A renamed executable must not register a companion that would launch a different fixed target.");
        }

        private static void TestStartupLauncher()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Portable Apps 中文");
            string executable = Path.Combine(Path.GetFullPath(directory), "Push To Talk.exe");
            System.Diagnostics.ProcessStartInfo info = StartupLauncher.CreateStartInfo(directory);
            Check(info.FileName == executable && Path.IsPathRooted(info.FileName), "The launcher target must be an absolute sibling path, including spaces and Unicode.");
            Check(info.WorkingDirectory == Path.GetFullPath(directory), "Startup must not depend on the Windows logon working directory.");
            Check(info.UseShellExecute && info.Verb == "runas", "Startup must explicitly request administrator approval through the Windows shell.");
            Check(info.Arguments == "--startup", "Only the fixed startup flag may be passed to the elevated main app.");
            Check(info.WindowStyle == System.Diagnostics.ProcessWindowStyle.Normal && !info.CreateNoWindow,
                "The launcher must not hide the elevated application's error or approval-related UI.");
            int launches = 0;
            bool started = StartupLauncher.Launch(directory, delegate(string path) { return path == executable; },
                delegate(System.Diagnostics.ProcessStartInfo command) { launches++; Check(command.FileName == executable, "The validated target must be the one launched."); });
            Check(started && launches == 1, "A successful request must launch exactly once.");
            launches = 0;
            bool cancelled = StartupLauncher.Launch(directory, delegate(string path) { return true; },
                delegate(System.Diagnostics.ProcessStartInfo command) { launches++; throw new System.ComponentModel.Win32Exception(1223); });
            Check(!cancelled && launches == 1, "UAC cancellation must exit after one attempt without retrying or falling back to ordinary privileges.");
            bool failed = false;
            launches = 0;
            try
            {
                StartupLauncher.Launch(directory, delegate(string path) { return true; },
                    delegate(System.Diagnostics.ProcessStartInfo command) { launches++; throw new System.ComponentModel.Win32Exception(5); });
            }
            catch (System.ComponentModel.Win32Exception error) { failed = error.NativeErrorCode == 5; }
            Check(failed && launches == 1, "Non-cancellation launch errors must be reported without retrying.");
            failed = false;
            launches = 0;
            try
            {
                StartupLauncher.Launch(directory, delegate(string path) { return false; },
                    delegate(System.Diagnostics.ProcessStartInfo command) { launches++; });
            }
            catch (FileNotFoundException error) { failed = error.FileName == executable; }
            Check(failed && launches == 0, "A missing main app must fail before any elevation request.");
            bool invalid = false;
            try { StartupLauncher.CreateStartInfo(" "); }
            catch (ArgumentException) { invalid = true; }
            Check(invalid, "An empty startup directory must be rejected.");

            string pathForSettings = Path.Combine(Path.GetTempPath(), "PushToTalk-Launcher-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Check(StartupLauncher.ReadLanguage(pathForSettings) == "system", "Fresh launcher settings must follow the system language.");
                foreach (string language in new string[] { "en-US", "zh-CN", "system", "unexpected" })
                {
                    string content = "<AppSettings><Language>" + language + "</Language><Bindings><string>V</string></Bindings></AppSettings>";
                    File.WriteAllText(pathForSettings, content);
                    Check(StartupLauncher.ReadLanguage(pathForSettings) == Localization.NormalizeLanguage(language), "The companion must respect the saved interface language.");
                    Check(File.ReadAllText(pathForSettings) == content, "Reading launch language must not modify user settings.");
                }
                File.WriteAllText(pathForSettings, "<AppSettings><Bindings><string>F2</string></Bindings></AppSettings>");
                Check(StartupLauncher.ReadLanguage(pathForSettings) == "system", "Legacy settings without a language must follow the system.");
                File.WriteAllText(pathForSettings, "<Other><Language>en-US</Language></Other>");
                Check(StartupLauncher.ReadLanguage(pathForSettings) == "system", "Unrelated XML must not be treated as application settings.");
                File.WriteAllText(pathForSettings, "<AppSettings><Language>");
                Check(StartupLauncher.ReadLanguage(pathForSettings) == "system", "Malformed settings must not prevent startup.");
                File.WriteAllText(pathForSettings, "<!DOCTYPE AppSettings [<!ENTITY language 'en-US'>]><AppSettings><Language>&language;</Language></AppSettings>");
                Check(StartupLauncher.ReadLanguage(pathForSettings) == "system", "The startup language reader must reject DTD entities.");
                File.WriteAllText(pathForSettings, "<AppSettings><Padding>" + new string('x', 262145) + "</Padding><Language>en-US</Language></AppSettings>");
                Check(StartupLauncher.ReadLanguage(pathForSettings) == "system", "Oversized settings must not be parsed by the startup companion.");
            }
            finally { if (File.Exists(pathForSettings)) File.Delete(pathForSettings); }
        }

        private sealed class StartupStore : IStartupRegistrationStore
        {
            internal object Value;
            internal int Writes;
            internal int Deletes;
            internal bool FailWrite;
            public object Read() { return Value; }
            public void Write(string commandLine)
            {
                if (FailWrite) throw new IOException("Test store write failure.");
                Writes++;
                Value = commandLine;
            }
            public void Delete() { Deletes++; Value = null; }
        }

        private static void TestSoundFeedbackTransitions()
        {
            foreach (bool enabled in new bool[] { false, true })
            {
                List<bool> cues = new List<bool>();
                using (SoundFeedback feedback = new SoundFeedback(delegate(bool muted) { cues.Add(muted); }))
                {
                    Check(!feedback.Enabled, "Sound feedback must start disabled.");
                    feedback.Enabled = enabled;
                    MuteStateMachine state = new MuteStateMachine(ControlMode.PushToTalk, Bindings("F2", "MouseX1"), false);
                    bool previousMute = true;
                    SoundSample(state, feedback, ref previousMute);
                    Check(cues.Count == 0, "Initial release gate must not play sounds.");
                    SoundSample(state, feedback, ref previousMute, Keys.F2);
                    Check(cues.Count == (enabled ? 1 : 0), "First shortcut press must play one opening cue only when enabled.");
                    for (int i = 0; i < 30; i++) SoundSample(state, feedback, ref previousMute, Keys.F2);
                    Check(cues.Count == (enabled ? 1 : 0), "Holding a shortcut and repeated watchdog samples must not repeat a cue.");
                    SoundSample(state, feedback, ref previousMute, Keys.F2, Keys.XButton1);
                    SoundSample(state, feedback, ref previousMute, Keys.XButton1);
                    Check(cues.Count == (enabled ? 1 : 0), "Adding and releasing overlapping shortcuts must not announce false transitions.");
                    SoundSample(state, feedback, ref previousMute);
                    for (int i = 0; i < 30; i++) SoundSample(state, feedback, ref previousMute);
                    Check(cues.Count == (enabled ? 2 : 0), "Only final release must add a closing cue; idle polling must stay silent.");
                    if (enabled) Check(!cues[0] && cues[1], "Opening and closing cues must receive different microphone states in order.");
                }
            }
        }

        private static void TestSoundFeedbackSwitchAndDisposal()
        {
            List<bool> cues = new List<bool>();
            SoundFeedback feedback = new SoundFeedback(delegate(bool muted) { cues.Add(muted); });
            try
            {
                MuteStateMachine state = new MuteStateMachine(ControlMode.PushToTalk, Bindings("F2"), false);
                bool previousMute = true;
                SoundSample(state, feedback, ref previousMute);
                SoundSample(state, feedback, ref previousMute, Keys.F2);
                feedback.Enabled = true;
                Check(feedback.Enabled && cues.Count == 0, "Enabling sounds while the microphone is open must not replay its current state.");
                SoundSample(state, feedback, ref previousMute, Keys.F2);
                Check(cues.Count == 0, "A held key after enabling sounds must remain silent.");
                SoundSample(state, feedback, ref previousMute);
                Check(cues.Count == 1 && cues[0], "The next actual closing transition must play after enabling sounds.");
                feedback.Enabled = false;
                Check(cues.Count == 1, "Disabling sounds must not itself play a cue.");
                SoundSample(state, feedback, ref previousMute, Keys.F2);
                SoundSample(state, feedback, ref previousMute);
                Check(cues.Count == 1, "Disabled sounds must suppress both opening and closing transitions.");
                feedback.Enabled = true;
                Check(cues.Count == 1, "Re-enabling sounds must not replay a muted current state.");
                SoundSample(state, feedback, ref previousMute, Keys.F2);
                Check(cues.Count == 2 && !cues[1], "A fresh opening transition must still work after re-enabling sounds.");
                feedback.Dispose();
                feedback.Dispose();
                feedback.Enabled = true;
                feedback.PlayTransition(false, true);
                Check(!feedback.Enabled && cues.Count == 2, "Disposed sound feedback must remain silent and cannot be re-enabled.");
            }
            finally { feedback.Dispose(); }
        }

        private static void SoundSample(MuteStateMachine state, SoundFeedback feedback, ref bool previousMute, params Keys[] keys)
        {
            bool currentMute = state.Update(Down(keys));
            feedback.PlayTransition(previousMute, currentMute);
            previousMute = currentMute;
        }

        private static bool Active(HotkeyBinding[] bindings, params Keys[] keys)
        {
            return HotkeyInput.IsAnyBindingDown(bindings, Down(keys));
        }

        private static HashSet<Keys> Down(params Keys[] keys) { return new HashSet<Keys>(keys); }
        private static HotkeyBinding[] Bindings(params string[] text)
        {
            HotkeyBinding[] bindings = new HotkeyBinding[text.Length];
            for (int i = 0; i < text.Length; i++) bindings[i] = HotkeyBinding.Parse(text[i]);
            return bindings;
        }

        private static void Check(bool passed, string message)
        {
            assertions++;
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}

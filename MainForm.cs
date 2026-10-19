using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PushToTalk
{
    public sealed class MainForm : Form
    {
        private static readonly Color Ink = DarkTheme.Text;
        private static readonly Color Subtle = DarkTheme.Subtle;
        private static readonly Color Blue = DarkTheme.Accent;
        private readonly ComboBox devices = new ComboBox();
        private readonly ComboBox modes = new ComboBox();
        private readonly ComboBox language = new ComboBox();
        private readonly ListBox bindingList = new ListBox();
        private readonly Label emptyHint = new Label();
        private readonly Label status = new Label();
        private readonly Label detail = new Label();
        private readonly Label help = new Label();
        private readonly Label modeHelp = new Label();
        private readonly Button start = new Button();
        private readonly Button add = new Button();
        private readonly Button remove = new Button();
        private readonly Button refresh = new Button();
        private readonly CheckBox enableSounds = new CheckBox();
        private readonly CheckBox startWithWindows = new CheckBox();
        private readonly CheckBox enableOverlay = new CheckBox();
        private readonly Button editOverlay = new Button();
        private readonly Panel overlayEditor = new Panel();
        private readonly TrackBar overlayOpacity = new TrackBar();
        private readonly Label overlayOpacityValue = new Label();
        private readonly List<Control> overlayBottomControls = new List<Control>();
        private readonly StartupRegistration startupRegistration = new StartupRegistration();
        private readonly SoundFeedback feedback = new SoundFeedback();
        private readonly Panel statusPanel = new Panel();
        private readonly Timer timer = new Timer();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem trayToggle = new ToolStripMenuItem(M("启用控制"));
        private readonly List<HotkeyBinding> bindings = new List<HotkeyBinding>();
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
        private AppSettings settings;
        private AudioSession session;
        private MuteStateMachine state;
        private HotkeyPoller poller;
        private bool originalMute;
        private bool hasOriginalMute;
        private bool lastMute = true;
        private bool paused;
        private bool exiting;
        private bool loading;
        private bool disposedResources;
        private bool suppressInitialShow;
        private bool updatingStartup;
        private bool updatingOverlay;
        private int overlayEditorShift;
        private OverlayForm overlay;
        private int watchdog;
        private Icon idleIcon;
        private Icon mutedIcon;
        private Icon liveIcon;

        public MainForm() : this(false) { }

        public MainForm(bool startupLaunch)
        {
            suppressInitialShow = startupLaunch;
            Text = "Push To Talk";
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = Ink;
            BackColor = DarkTheme.Background;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 729);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DarkTheme.ApplyTitleBar(this);

            idleIcon = AppIcon.Load();
            mutedIcon = MakeIcon(Blue);
            liveIcon = MakeIcon(Color.FromArgb(21, 148, 102));
            Icon = idleIcon;
            Label appTitle = LabelAt("Push To Talk", 24, 20, 242, 42, 19, true);
            appTitle.Font = new Font("Segoe UI", 19F, FontStyle.Bold);
            Controls.Add(appTitle);
            language.SetBounds(284, 27, 148, 30);
            language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Items.AddRange(new object[] { "系统 / System", "简体中文", "English" });
            language.AccessibleName = M("界面语言");
            language.AccessibleDescription = M("跟随系统，或手动选择中文和英文。");
            DarkTheme.StyleComboBox(language);
            Controls.Add(language);
            Button about = new Button();
            about.SetBounds(444, 27, 92, 31);
            StyleButton(about, M("关于"), false);
            about.Click += delegate { using (AboutDialog dialog = new AboutDialog()) dialog.ShowDialog(this); };
            Controls.Add(about);
            Label subtitle = LabelAt(M("多个按键，一个麦克风。"), 26, 63, 510, 24, 10, false);
            subtitle.ForeColor = Subtle;
            Controls.Add(subtitle);

            statusPanel.SetBounds(24, 103, 512, 80);
            statusPanel.BackColor = DarkTheme.Surface;
            status.SetBounds(16, 11, 480, 30);
            status.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
            detail.SetBounds(17, 47, 478, 24);
            detail.ForeColor = Subtle;
            statusPanel.Controls.AddRange(new Control[] { status, detail });
            Controls.Add(statusPanel);

            Controls.Add(LabelAt(M("麦克风"), 24, 201, 512, 23, 10, true));
            devices.SetBounds(24, 230, 412, 30);
            devices.DropDownStyle = ComboBoxStyle.DropDownList;
            devices.DropDownWidth = 650;
            devices.AccessibleName = M("麦克风");
            DarkTheme.StyleComboBox(devices);
            refresh.SetBounds(448, 228, 88, 31);
            StyleButton(refresh, M("刷新"), false);
            refresh.Click += delegate { RefreshDevices(); };
            Controls.AddRange(new Control[] { devices, refresh });

            Controls.Add(LabelAt(M("控制方式"), 24, 276, 512, 23, 10, true));
            modes.SetBounds(24, 305, 512, 30);
            modes.DropDownStyle = ComboBoxStyle.DropDownList;
            modes.Items.AddRange(new object[] { M("按住说话"), M("按住静音"), M("按一下切换开 / 关") });
            modes.AccessibleName = M("控制方式");
            DarkTheme.StyleComboBox(modes);
            Controls.Add(modes);
            modeHelp.SetBounds(25, 342, 510, 24);
            modeHelp.ForeColor = Subtle;
            Controls.Add(modeHelp);

            Controls.Add(LabelAt(M("快捷键"), 24, 383, 295, 26, 10, true));
            add.SetBounds(322, 378, 102, 31);
            StyleButton(add, M("+ 添加按键"), false);
            add.Click += AddBinding;
            remove.SetBounds(436, 378, 100, 31);
            StyleButton(remove, M("移除所选"), false);
            remove.Click += RemoveBinding;
            Controls.AddRange(new Control[] { add, remove });
            bindingList.SetBounds(24, 421, 512, 111);
            bindingList.BorderStyle = BorderStyle.FixedSingle;
            bindingList.IntegralHeight = false;
            bindingList.Font = new Font("Segoe UI", 12F);
            bindingList.ItemHeight = 27;
            bindingList.HorizontalScrollbar = true;
            bindingList.AccessibleName = M("已绑定快捷键");
            DarkTheme.StyleListBox(bindingList);
            bindingList.SelectedIndexChanged += delegate { UpdateButtons(); };
            Controls.Add(bindingList);
            emptyHint.SetBounds(35, 450, 488, 50);
            emptyHint.Text = M("还没有绑定按键\r\n点击“添加按键”，自行设置第一个快捷键。");
            emptyHint.ForeColor = Subtle;
            emptyHint.BackColor = DarkTheme.Field;
            emptyHint.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(emptyHint);
            emptyHint.BringToFront();
            help.SetBounds(25, 543, 510, 23);
            help.ForeColor = Subtle;
            help.Text = M("多个快捷键分别添加；按住任意一个即可说话。");
            Controls.Add(help);

            enableSounds.SetBounds(26, 568, 280, 24);
            enableSounds.Text = M("启用提示音（开麦 / 静音）");
            enableSounds.AccessibleName = M("启用提示音");
            DarkTheme.StyleCheckBox(enableSounds);
            Controls.Add(enableSounds);
            startWithWindows.SetBounds(322, 568, 214, 24);
            startWithWindows.Text = M("开机自启动");
            startWithWindows.AccessibleName = M("开机自启动");
            startWithWindows.AccessibleDescription = M("登录 Windows 后在托盘运行，有已保存的快捷键和可用麦克风时自动启用控制。");
            DarkTheme.StyleCheckBox(startWithWindows);
            Controls.Add(startWithWindows);

            enableOverlay.SetBounds(26, 602, 280, 24);
            enableOverlay.Text = M("启用屏幕覆盖");
            enableOverlay.AccessibleName = M("启用屏幕覆盖");
            enableOverlay.AccessibleDescription = M("在屏幕上显示开麦、静音或未启用状态，默认关闭。");
            DarkTheme.StyleCheckBox(enableOverlay);
            Controls.Add(enableOverlay);
            editOverlay.SetBounds(322, 598, 214, 31);
            StyleButton(editOverlay, M("编辑位置和大小"), false);
            editOverlay.AccessibleDescription = M("编辑时拖动图标移动，拖动边框或角点缩放。");
            editOverlay.Click += delegate { ToggleOverlayEditing(); };
            editOverlay.Enabled = false;
            Controls.Add(editOverlay);

            overlayEditor.SetBounds(24, 636, 512, 40);
            overlayEditor.Visible = false;
            Label opacityLabel = LabelAt(M("不透明度"), 2, 9, 85, 23, 9, false);
            overlayOpacity.SetBounds(92, 0, 332, 40);
            overlayOpacity.BackColor = DarkTheme.Background;
            overlayOpacity.TickStyle = TickStyle.None;
            overlayOpacity.Minimum = OverlayLayout.MinimumOpacity;
            overlayOpacity.Maximum = OverlayLayout.MaximumOpacity;
            overlayOpacity.Value = OverlayLayout.DefaultOpacity;
            overlayOpacity.SmallChange = 5;
            overlayOpacity.LargeChange = 10;
            overlayOpacity.AccessibleName = M("屏幕覆盖不透明度");
            overlayOpacityValue.SetBounds(444, 9, 68, 23);
            overlayOpacityValue.TextAlign = ContentAlignment.TopRight;
            overlayOpacityValue.Text = "100%";
            overlayEditor.Controls.AddRange(new Control[] { opacityLabel, overlayOpacity, overlayOpacityValue });
            Controls.Add(overlayEditor);

            start.SetBounds(24, 646, 296, 42);
            StyleButton(start, M("启用按住说话"), true);
            start.Click += delegate { ToggleControl(); };
            Button hide = new Button();
            hide.SetBounds(332, 646, 115, 42);
            StyleButton(hide, M("收起到托盘"), false);
            hide.Click += delegate { HideToTray(); };
            Button exit = new Button();
            exit.SetBounds(459, 646, 77, 42);
            StyleButton(exit, M("退出"), false);
            exit.Click += delegate { ExitApp(); };
            Controls.AddRange(new Control[] { start, hide, exit });
            Label footer = LabelAt(M("关闭窗口后继续在托盘运行。停用或退出时恢复原先的静音状态。"), 25, 699, 515, 24, 8, false);
            footer.ForeColor = Subtle;
            Controls.Add(footer);
            overlayBottomControls.AddRange(new Control[] { start, hide, exit, footer });

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(M("打开主窗口"), null, delegate { ShowMain(); });
            trayToggle.Click += delegate { ToggleControl(); };
            menu.Items.Add(trayToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(M("退出"), null, delegate { ExitApp(); });
            DarkTheme.StyleMenu(menu);
            tray.ContextMenuStrip = menu;
            tray.Icon = idleIcon;
            tray.Text = M("Push To Talk · 未启用");
            tray.DoubleClick += delegate { ShowMain(); };
            tray.Visible = true;
            timer.Interval = 20;
            timer.Tick += Tick;

            loading = true;
            string loadError;
            settings = AppSettings.Load(settingsPath, out loadError);
            Localization.SetLanguage(settings.Language);
            language.SelectedIndex = settings.Language == "zh-CN" ? 1 : settings.Language == "en-US" ? 2 : 0;
            enableOverlay.Checked = settings.EnableOverlay;
            overlayOpacity.Value = settings.OverlayOpacity;
            overlayOpacityValue.Text = overlayOpacity.Value + "%";
            string startupError = null;
            try { startWithWindows.Checked = startupRegistration.IsEnabled(Application.ExecutablePath); }
            catch (Exception) { startWithWindows.Enabled = false; startupError = M("无法读取开机启动设置。"); }
            enableSounds.Checked = settings.EnableSounds;
            feedback.Enabled = settings.EnableSounds;
            foreach (string saved in settings.Bindings)
            {
                HotkeyBinding binding;
                if (HotkeyBinding.TryParse(saved, out binding) && !ContainsBinding(binding)) bindings.Add(binding);
            }
            modes.SelectedIndex = (int)settings.Mode;
            RefreshDevices();
            RebuildBindings();
            loading = false;
            modes.SelectedIndexChanged += delegate { if (loading) return; UpdateMode(); SaveSettings(); };
            devices.SelectedIndexChanged += delegate { SaveSettings(); UpdateButtons(); };
            enableSounds.CheckedChanged += delegate { feedback.Enabled = enableSounds.Checked; SaveSettings(); };
            startWithWindows.CheckedChanged += ChangeStartup;
            language.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Localization.SetLanguage(language.SelectedIndex == 1 ? "zh-CN" : language.SelectedIndex == 2 ? "en-US" : "system");
                ApplyLanguage();
                SaveSettings();
            };
            overlayOpacity.ValueChanged += delegate
            {
                overlayOpacityValue.Text = overlayOpacity.Value + "%";
                if (loading || overlay == null) return;
                try { overlay.SetOpacityPercent(overlayOpacity.Value); }
                catch (Exception error) { OverlayError(error); }
            };
            overlayOpacity.MouseUp += delegate { if (!loading) SaveSettings(); };
            overlayOpacity.KeyUp += delegate { if (!loading) SaveSettings(); };
            enableOverlay.CheckedChanged += delegate
            {
                if (loading || updatingOverlay) return;
                ApplyOverlayVisibility();
                SaveSettings();
            };
            UpdateMode();
            SetIdle(loadError ?? startupError ?? (devices.SelectedItem == null ? detail.Text : M("设置好按键后，点击下方按钮启用。")));
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };
            FormClosing += HandleFormClosing;
            ApplyOverlayVisibility();
            ApplyLanguage(true);
        }

        private static readonly Dictionary<string, string> englishText = new Dictionary<string, string>
        {
            { "界面语言", "Interface language" },
            { "跟随系统，或手动选择中文和英文。", "Follow the system language, or choose Chinese or English." },
            { "启用控制", "Enable control" },
            { "关于", "About" },
            { "多个按键，一个麦克风。", "Multiple hotkeys. One microphone." },
            { "麦克风", "Microphone" },
            { "刷新", "Refresh" },
            { "控制方式", "Control mode" },
            { "按住说话", "Push to talk" },
            { "按住静音", "Push to mute" },
            { "按一下切换开 / 关", "Toggle on / off" },
            { "快捷键", "Hotkeys" },
            { "+ 添加按键", "+ Add hotkey" },
            { "移除所选", "Remove" },
            { "已绑定快捷键", "Assigned hotkeys" },
            { "还没有绑定按键\r\n点击“添加按键”，自行设置第一个快捷键。", "No hotkeys yet\r\nChoose Add hotkey to create your first binding." },
            { "多个快捷键分别添加；按住任意一个即可说话。", "Add hotkeys separately; hold any one to talk." },
            { "多个快捷键分别添加；每行代表一种独立触发方式。", "Each row is a separate way to trigger the selected mode." },
            { "启用提示音（开麦 / 静音）", "Sound cues (unmute / mute)" },
            { "启用提示音", "Sound cues" },
            { "开机自启动", "Start with Windows" },
            { "登录 Windows 后在托盘运行，有已保存的快捷键和可用麦克风时自动启用控制。", "Start in the tray at sign-in; enable control when saved hotkeys and microphone are available." },
            { "启用屏幕覆盖", "Screen overlay" },
            { "在屏幕上显示开麦、静音或未启用状态，默认关闭。", "Show microphone status on screen. Off by default." },
            { "编辑位置和大小", "Edit position and size" },
            { "编辑时拖动图标移动，拖动边框或角点缩放。", "Drag the icon to move; drag its edges or corners to resize." },
            { "不透明度", "Opacity" },
            { "屏幕覆盖不透明度", "Overlay opacity" },
            { "启用按住说话", "Enable push to talk" },
            { "收起到托盘", "Hide to tray" },
            { "退出", "Exit" },
            { "关闭窗口后继续在托盘运行。停用或退出时恢复原先的静音状态。", "Close to tray. Disabling or exiting restores the previous mute state." },
            { "打开主窗口", "Open main window" },
            { "Push To Talk · 未启用", "Push To Talk · Disabled" },
            { "无法读取开机启动设置。", "Cannot read Windows startup settings." },
            { "设置好按键后，点击下方按钮启用。", "Set your hotkeys, then enable control below." },
            { "无法更新开机自启动设置：", "Cannot update Windows startup settings: " },
            { "任意一个绑定键按住时开麦，全部松开后静音。", "Hold any hotkey to talk; release all to mute." },
            { "任意一个绑定键按住时静音，全部松开后开麦。", "Hold any hotkey to mute; release all to unmute." },
            { "按下任意一个绑定键，切换一次麦克风开关。", "Press any hotkey to toggle the microphone on or off." },
            { "未检测到麦克风，请连接设备后刷新。", "No microphone found. Connect one, then refresh." },
            { "请重新选择要控制的麦克风。", "Select the microphone you want to control." },
            { "这个快捷键已经添加。", "This hotkey is already assigned." },
            { "设置无法保存，请将程序放在有写入权限的文件夹中。", "Cannot save settings. Move the app to a writable folder." },
            { "屏幕覆盖已关闭：", "Screen overlay disabled: " },
            { "完成编辑", "Done editing" },
            { "拖动图标移动，拖动边框或角点缩放。", "Drag the icon to move; drag its edges or corners to resize." },
            { "已暂停", "Paused" },
            { "已静音", "Muted" },
            { "正在开麦", "Mic on" },
            { "锁屏或休眠后已暂停；停用后可重新启用。", "Paused after lock or sleep. Disable, then enable to resume." },
            { "请先松开所有绑定键。", "Release all assigned hotkeys first." },
            { "已停用，麦克风恢复为启用前的静音状态。", "Disabled. The previous microphone mute state was restored." },
            { "麦克风状态恢复失败，请检查设备的静音状态。", "Could not restore the microphone. Check its mute state." },
            { "控制已停止：", "Control stopped: " },
            { "麦克风控制已停止，请打开主窗口检查设备。", "Microphone control stopped. Open the app to check the device." },
            { "未启用", "Disabled" },
            { "停用控制", "Disable control" }
        };
        private static readonly Dictionary<string, string> chineseText = ReverseTranslations();

        private static Dictionary<string, string> ReverseTranslations()
        {
            Dictionary<string, string> reverse = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> entry in englishText) reverse[entry.Value] = entry.Key;
            return reverse;
        }

        private static string M(string text)
        {
            if (text == null) return null;
            string translated;
            if (englishText.TryGetValue(text, out translated)) return Localization.Text(text, translated);
            if (chineseText.TryGetValue(text, out translated)) return Localization.Text(translated, text);
            return text;
        }

        private static void TranslateControls(Control root)
        {
            if (!(root is ComboBox) && !(root is ListBox) && !(root is TrackBar)) root.Text = M(root.Text);
            root.AccessibleName = M(root.AccessibleName);
            root.AccessibleDescription = M(root.AccessibleDescription);
            foreach (Control child in root.Controls) TranslateControls(child);
        }

        private void ApplyLanguage(bool preserveIdleMessage = false)
        {
            bool previousLoading = loading;
            loading = true;
            try
            {
                TranslateControls(this);
                int selectedMode = modes.SelectedIndex;
                modes.Items.Clear();
                modes.Items.AddRange(new object[] { M("按住说话"), M("按住静音"), M("按一下切换开 / 关") });
                modes.SelectedIndex = Math.Max(0, selectedMode);
                if (tray.ContextMenuStrip != null)
                    foreach (ToolStripItem item in tray.ContextMenuStrip.Items) item.Text = M(item.Text);
                if (overlay != null) overlay.Text = Localization.Text("Push To Talk 屏幕覆盖", "Push To Talk Overlay");
                UpdateMode();
                UpdateOverlayEditorUi();
                if (session != null) SetLiveStatus(lastMute);
                else SetIdle(preserveIdleMessage ? M(detail.Text) : M(devices.SelectedItem == null ?
                    "请重新选择要控制的麦克风。" : "设置好按键后，点击下方按钮启用。"));
            }
            finally { loading = previousLoading; }
        }

        private Label LabelAt(string text, int x, int y, int width, int height, float size, bool bold)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular) };
        }

        private static void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text;
            DarkTheme.StyleButton(button, primary);
            button.Cursor = Cursors.Hand;
        }

        private void ChangeStartup(object sender, EventArgs args)
        {
            if (loading || updatingStartup) return;
            bool requested = startWithWindows.Checked;
            try { startupRegistration.SetEnabled(Application.ExecutablePath, requested); }
            catch (Exception error)
            {
                updatingStartup = true;
                startWithWindows.Checked = !requested;
                updatingStartup = false;
                MessageBox.Show(this, M("无法更新开机自启动设置：") + error.Message,
                    "Push To Talk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private ControlMode Mode { get { return (ControlMode)Math.Max(0, modes.SelectedIndex); } }

        private void UpdateMode()
        {
            string[] descriptions = {
                M("任意一个绑定键按住时开麦，全部松开后静音。"),
                M("任意一个绑定键按住时静音，全部松开后开麦。"),
                M("按下任意一个绑定键，切换一次麦克风开关。")
            };
            modeHelp.Text = descriptions[(int)Mode];
            help.Text = Mode == ControlMode.PushToTalk ? M("多个快捷键分别添加；按住任意一个即可说话。") : M("多个快捷键分别添加；每行代表一种独立触发方式。");
            UpdateButtons();
        }

        private void RefreshDevices()
        {
            if (session != null) return;
            string previous = devices.SelectedItem is MicrophoneInfo ? ((MicrophoneInfo)devices.SelectedItem).Id : settings.MicrophoneId;
            bool wasLoading = loading;
            loading = true;
            try
            {
                List<MicrophoneInfo> list = AudioDevices.EnumerateMicrophones();
                string defaultId = AudioDevices.GetDefaultMicrophoneId();
                devices.Items.Clear();
                foreach (MicrophoneInfo microphone in list) devices.Items.Add(microphone);
                int index = -1;
                for (int i = 0; i < list.Count; i++) if (list[i].Id == previous) index = i;
                // If a previously chosen device disappeared, require another explicit selection.
                if (index < 0 && String.IsNullOrEmpty(previous))
                    for (int i = 0; i < list.Count; i++) if (list[i].Id == defaultId) index = i;
                if (index < 0 && String.IsNullOrEmpty(previous) && list.Count == 1) index = 0;
                devices.SelectedIndex = index;
                if (list.Count == 0) SetIdle(M("未检测到麦克风，请连接设备后刷新。"));
                else if (index < 0) SetIdle(M("请重新选择要控制的麦克风。"));
            }
            catch (Exception e) { SetIdle(e.Message); }
            finally { loading = wasLoading; UpdateButtons(); }
        }

        private bool ContainsBinding(HotkeyBinding value)
        {
            foreach (HotkeyBinding binding in bindings) if (binding.Serialize() == value.Serialize()) return true;
            return false;
        }

        private void AddBinding(object sender, EventArgs args)
        {
            using (CaptureDialog dialog = new CaptureDialog())
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Binding != null)
                {
                    if (ContainsBinding(dialog.Binding)) { detail.Text = M("这个快捷键已经添加。"); return; }
                    bindings.Add(dialog.Binding);
                    RebuildBindings();
                    bindingList.SelectedIndex = bindings.Count - 1;
                    SaveSettings();
                }
        }

        private void RemoveBinding(object sender, EventArgs args)
        {
            if (session != null || bindingList.SelectedIndex < 0) return;
            bindings.RemoveAt(bindingList.SelectedIndex);
            RebuildBindings();
            SaveSettings();
        }

        private void RebuildBindings()
        {
            bindingList.Items.Clear();
            foreach (HotkeyBinding binding in bindings) bindingList.Items.Add(binding);
            emptyHint.Visible = bindings.Count == 0;
            UpdateButtons();
        }

        private void SaveSettings()
        {
            if (loading) return;
            settings.Language = Localization.Choice;
            settings.Mode = Mode;
            settings.EnableSounds = enableSounds.Checked;
            settings.EnableOverlay = enableOverlay.Checked;
            settings.OverlayOpacity = overlayOpacity.Value;
            CaptureOverlayBounds();
            settings.MicrophoneId = devices.SelectedItem is MicrophoneInfo ? ((MicrophoneInfo)devices.SelectedItem).Id : settings.MicrophoneId;
            settings.Bindings.Clear();
            foreach (HotkeyBinding binding in bindings) settings.Bindings.Add(binding.Serialize());
            try { settings.Save(settingsPath); }
            catch (Exception) { detail.Text = M("设置无法保存，请将程序放在有写入权限的文件夹中。"); }
        }

        private static Rectangle[] ScreenWorkAreas()
        {
            List<Rectangle> areas = new List<Rectangle>();
            if (Screen.PrimaryScreen != null) areas.Add(Screen.PrimaryScreen.WorkingArea);
            foreach (Screen screen in Screen.AllScreens)
                if (!screen.Primary) areas.Add(screen.WorkingArea);
            return areas.ToArray();
        }

        private void CaptureOverlayBounds()
        {
            if (overlay == null || overlay.IsDisposed || settings == null) return;
            settings.OverlayX = overlay.Left;
            settings.OverlayY = overlay.Top;
            settings.OverlaySize = overlay.Width;
            settings.OverlayOpacity = overlay.OpacityPercent;
        }

        private void ApplyOverlayVisibility()
        {
            try
            {
                if (!enableOverlay.Checked)
                {
                    CloseOverlay();
                    return;
                }
                if (overlay == null)
                {
                    overlay = new OverlayForm();
                    overlay.Bounds = OverlayLayout.GetBounds(settings.OverlayX, settings.OverlayY,
                        settings.OverlaySize, ScreenWorkAreas());
                    overlay.SetOpacityPercent(settings.OverlayOpacity);
                    overlay.LayoutChanged += OverlayLayoutChanged;
                    overlay.SetStatus(session != null, lastMute);
                    overlay.Show();
                }
                UpdateOverlayEditorUi();
            }
            catch (Exception error) { OverlayError(error); }
        }

        private void CloseOverlay()
        {
            CaptureOverlayBounds();
            OverlayForm previous = overlay;
            overlay = null;
            if (previous != null)
            {
                previous.LayoutChanged -= OverlayLayoutChanged;
                previous.Dispose();
            }
            UpdateOverlayEditorUi();
        }

        private void UpdateOverlayStatus()
        {
            if (overlay == null) return;
            try { overlay.SetStatus(session != null, lastMute); }
            catch (Exception error) { OverlayError(error); }
        }

        private void OverlayError(Exception error)
        {
            updatingOverlay = true;
            enableOverlay.Checked = false;
            try { CloseOverlay(); }
            finally { updatingOverlay = false; }
            SaveSettings();
            detail.Text = M("屏幕覆盖已关闭：") + error.Message;
        }

        private void ToggleOverlayEditing()
        {
            if (overlay == null) return;
            try
            {
                overlay.SetEditMode(!overlay.Editing);
                UpdateOverlayEditorUi();
                if (!overlay.Editing) SaveSettings();
            }
            catch (Exception error) { OverlayError(error); }
        }

        private void FinishOverlayEditing()
        {
            if (overlay == null || !overlay.Editing) return;
            overlay.SetEditMode(false);
            UpdateOverlayEditorUi();
            SaveSettings();
        }

        private void UpdateOverlayEditorUi()
        {
            bool editing = overlay != null && overlay.Editing;
            editOverlay.Enabled = overlay != null;
            editOverlay.Text = editing ? M("完成编辑") : M("编辑位置和大小");
            help.Text = editing ? M("拖动图标移动，拖动边框或角点缩放。") :
                (Mode == ControlMode.PushToTalk ? M("多个快捷键分别添加；按住任意一个即可说话。") :
                M("多个快捷键分别添加；每行代表一种独立触发方式。"));
            if (IsDisposed || Disposing) return;
            if (editing && overlayEditorShift == 0)
            {
                int margin;
                using (Graphics graphics = CreateGraphics()) margin = (int)Math.Round(8 * graphics.DpiY / 96);
                overlayEditorShift = overlayEditor.Height + margin;
                foreach (Control control in overlayBottomControls) control.Top += overlayEditorShift;
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + overlayEditorShift);
                overlayEditor.Visible = true;
            }
            else if (!editing && overlayEditorShift != 0)
            {
                overlayEditor.Visible = false;
                foreach (Control control in overlayBottomControls) control.Top -= overlayEditorShift;
                ClientSize = new Size(ClientSize.Width, ClientSize.Height - overlayEditorShift);
                overlayEditorShift = 0;
            }
        }

        private void OverlayLayoutChanged(object sender, EventArgs args)
        {
            if (!loading && overlay != null) SaveSettings();
        }

        private void HideToTray()
        {
            try { FinishOverlayEditing(); }
            catch (Exception error) { OverlayError(error); }
            Hide();
        }

        private void DisplaysChanged(object sender, EventArgs args)
        {
            OnUiThread(delegate
            {
                if (overlay == null) return;
                try
                {
                    overlay.Bounds = OverlayLayout.GetBounds(overlay.Left, overlay.Top,
                        overlay.Width, ScreenWorkAreas());
                    SaveSettings();
                }
                catch (Exception error) { OverlayError(error); }
            });
        }

        private void ToggleControl()
        {
            ToggleControl(true);
        }

        private void ToggleControl(bool showErrorWindow)
        {
            if (session != null)
            {
                StopControl();
                return;
            }
            if (bindings.Count == 0 || devices.SelectedItem == null) return;
            try
            {
                session = new AudioSession(((MicrophoneInfo)devices.SelectedItem).Id);
                hasOriginalMute = false;
                originalMute = session.GetMute();
                hasOriginalMute = true;
                state = new MuteStateMachine(Mode, bindings, originalMute);
                poller = HotkeyInput.CreatePoller(bindings);
                // First require release of all triggers; startup never accidentally opens the mic.
                session.SetMute(true);
                lastMute = true;
                watchdog = 0;
                paused = false;
                timer.Start();
                SetLiveStatus(true);
                UpdateButtons();
            }
            catch (Exception e) { StopForError(e.Message); if (showErrorWindow) ShowMain(); }
        }

        private void Tick(object sender, EventArgs args)
        {
            if (session == null || paused) return;
            try
            {
                bool mute = state.Update(poller.GetPressedKeys());
                bool previousMute = lastMute;
                watchdog += timer.Interval;
                if (mute != lastMute || watchdog >= 500)
                {
                    if (mute != lastMute || session.GetMute() != mute) session.SetMute(mute);
                    watchdog = 0;
                    lastMute = mute;
                }
                feedback.PlayTransition(previousMute, mute);
                SetLiveStatus(mute);
            }
            catch (Exception e) { StopForError(e.Message); }
        }

        private void SetLiveStatus(bool mute)
        {
            UpdateOverlayStatus();
            string title = paused ? M("已暂停") : (mute ? M("已静音") : M("正在开麦"));
            if (status.Text != title)
            {
                status.Text = title;
                status.ForeColor = mute ? Blue : DarkTheme.LiveText;
                statusPanel.BackColor = mute ? DarkTheme.MutedSurface : DarkTheme.LiveSurface;
                tray.Icon = mute ? mutedIcon : liveIcon;
            }
            string trayText = "Push To Talk · " + title;
            if (tray.Text != trayText) tray.Text = trayText;
            string text = paused ? M("锁屏或休眠后已暂停；停用后可重新启用。") :
                (state != null && state.WaitingForRelease ? M("请先松开所有绑定键。") : modeHelp.Text);
            if (detail.Text != text) detail.Text = text;
        }

        private void StopControl()
        {
            string restoreError = EndSession();
            SetIdle(restoreError ?? M("已停用，麦克风恢复为启用前的静音状态。"));
        }

        private string EndSession()
        {
            timer.Stop();
            AudioSession previous = session;
            session = null;
            state = null;
            poller = null;
            paused = false;
            string error = null;
            if (previous != null)
            {
                try { if (hasOriginalMute) previous.SetMute(originalMute); }
                catch (Exception) { error = M("麦克风状态恢复失败，请检查设备的静音状态。"); }
                finally { previous.Dispose(); }
            }
            hasOriginalMute = false;
            UpdateButtons();
            return error;
        }

        public void StopForError(string message)
        {
            string restore = EndSession();
            SetIdle(M("控制已停止：") + message + (restore == null ? "" : " " + restore));
            if (!Visible) tray.ShowBalloonTip(5000, "Push To Talk", M("麦克风控制已停止，请打开主窗口检查设备。"), ToolTipIcon.Warning);
        }

        private void SetIdle(string message)
        {
            UpdateOverlayStatus();
            status.Text = M("未启用");
            status.ForeColor = Ink;
            statusPanel.BackColor = DarkTheme.Surface;
            detail.Text = message;
            tray.Icon = idleIcon;
            tray.Text = M("Push To Talk · 未启用");
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool active = session != null;
            start.Enabled = active || (bindings.Count > 0 && devices.SelectedItem != null);
            start.Text = active ? M("停用控制") : (Mode == ControlMode.PushToTalk ? M("启用按住说话") : M("启用控制"));
            trayToggle.Text = start.Text;
            trayToggle.Enabled = start.Enabled;
            add.Enabled = !active;
            remove.Enabled = !active && bindingList.SelectedIndex >= 0;
            devices.Enabled = !active;
            modes.Enabled = !active;
            refresh.Enabled = !active;
            bindingList.Enabled = !active;
        }

        private void PauseForSystem()
        {
            if (session == null || paused) return;
            timer.Stop();
            try { session.SetMute(true); lastMute = true; paused = true; SetLiveStatus(true); UpdateButtons(); }
            catch (Exception e) { StopForError(e.Message); }
        }

        private void OnUiThread(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { if (InvokeRequired) BeginInvoke(action); else action(); }
            catch (InvalidOperationException) { }
        }

        private void SessionSwitch(object sender, SessionSwitchEventArgs args)
        {
            if (args.Reason == SessionSwitchReason.SessionLock || args.Reason == SessionSwitchReason.RemoteDisconnect || args.Reason == SessionSwitchReason.ConsoleDisconnect)
                OnUiThread(PauseForSystem);
        }

        private void PowerChanged(object sender, PowerModeChangedEventArgs args)
        {
            if (args.Mode == PowerModes.Suspend) OnUiThread(PauseForSystem);
        }

        private void ShowMain()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (value && suppressInitialShow)
            {
                suppressInitialShow = false;
                if (!IsHandleCreated) CreateHandle();
                BeginInvoke(new Action(TryEnableOnStartup));
                value = false;
            }
            base.SetVisibleCore(value);
        }

        private void TryEnableOnStartup()
        {
            if (!IsDisposed && !Disposing && session == null && bindings.Count > 0 && devices.SelectedItem != null)
                ToggleControl(false);
        }

        private void ExitApp()
        {
            exiting = true;
            Close();
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs args)
        {
            if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; HideToTray(); return; }
            if (overlay != null) { CaptureOverlayBounds(); CloseOverlay(); }
            string error = EndSession();
            if (error != null && args.CloseReason == CloseReason.UserClosing)
                MessageBox.Show(this, error, "Push To Talk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            tray.Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposedResources)
            {
                disposedResources = true;
                SystemEvents.SessionSwitch -= SessionSwitch;
                SystemEvents.PowerModeChanged -= PowerChanged;
                SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
                CloseOverlay();
                EndSession();
                timer.Dispose();
                feedback.Dispose();
                tray.Visible = false;
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                tray.Dispose();
                if (idleIcon != null) idleIcon.Dispose();
                if (mutedIcon != null) mutedIcon.Dispose();
                if (liveIcon != null) liveIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        private static Icon MakeIcon(Color color)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Pen pen = new Pen(color, 3))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.FillEllipse(brush, 11, 3, 10, 10);
                    graphics.FillRectangle(brush, 11, 8, 10, 8);
                    graphics.FillEllipse(brush, 11, 11, 10, 10);
                    graphics.DrawArc(pen, 6, 8, 20, 18, 0, 180);
                    graphics.DrawLine(pen, 16, 26, 16, 29);
                    graphics.DrawLine(pen, 10, 29, 22, 29);
                }
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }
}

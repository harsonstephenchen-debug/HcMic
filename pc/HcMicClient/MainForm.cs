using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using HcMic.Core;

namespace HcMic
{
    public class MainForm : Form
    {
        private readonly WifiTransport _wifi = new();
        private readonly UsbTransport _usb = new() { AdbPath = FindAdb() };
        private readonly BluetoothTransport _bt = new();
        private PipeAudioSink _sink;
        private WasapiAudioSink _wasapi;
        private AudioFormat _fmt = new() { ProtocolVersion = Protocol.Version, SampleRate = 48000, Channels = 1, BitsPerSample = 16 };
        private TextBox _log;
        private ComboBox _mode;
        private TextBox _port;
        private Button _btnStart, _btnStop;
        private Label _stat;
        private MenuStrip _menu;
        private long _bytes;

        public MainForm()
        {
            Text = "HcMic Client";
            Size = new Size(560, 460);
            FormClosing += (s, e) => { try { _wifi.Stop(); _usb.Stop(); _bt.Stop(); _sink?.Dispose(); _wasapi?.Dispose(); } catch { } };

            // 顶部菜单栏：设置 / 帮助
            _menu = new MenuStrip();
            var mSettings = new ToolStripMenuItem("设置");
            var mHelp = new ToolStripMenuItem("帮助");
            mSettings.DropDownItems.Add(new ToolStripMenuItem("设置…", null, (s, e) => ShowSettings()));
            mHelp.DropDownItems.Add(new ToolStripMenuItem("关于 HcMic…", null, (s, e) =>
                MessageBox.Show("HcMic Client v1.1\r\n把手机麦克风变成电脑的虚拟麦克风。\r\n支持 WiFi / USB / 蓝牙传输。", "关于 HcMic",
                    MessageBoxButtons.OK, MessageBoxIcon.Information)));
            _menu.Items.AddRange(new ToolStripItem[] { mSettings, mHelp });
            MainMenuStrip = _menu;

            var lblMode = new Label { Text = "传输方式：", Left = 12, Top = 38, AutoSize = true };
            _mode = new ComboBox { Left = 90, Top = 35, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _mode.Items.AddRange(new object[] { "WiFi (TCP)", "USB (adb)", "蓝牙 (RFCOMM)" });
            _mode.SelectedIndex = 0;

            var lblPort = new Label { Text = "端口：", Left = 225, Top = 38, AutoSize = true };
            _port = new TextBox { Left = 270, Top = 35, Width = 70, Text = "51000" };

            _btnStart = new Button { Text = "启动", Left = 355, Top = 33, Width = 168 };
            _btnStop = new Button { Text = "停止", Left = 355, Top = 33, Width = 168, Enabled = false };
            _btnStart.Click += async (s, e) => await StartAsync();
            _btnStop.Click += (s, e) => StopAll();

            _stat = new Label { Text = "未连接", Left = 12, Top = 68, AutoSize = true, ForeColor = Color.Gray };

            _log = new TextBox { Left = 12, Top = 93, Width = 520, Height = 300, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font("Consolas", 9) };

            Controls.AddRange(new Control[] { lblMode, _mode, lblPort, _port, _btnStart, _btnStop, _stat, _log, _menu });

            _wifi.FrameReceived += OnFrame; _wifi.Status += s => Log("[WiFi] " + s);
            _usb.FrameReceived += OnFrame; _usb.Status += s => Log("[USB] " + s);
            _bt.FrameReceived += OnFrame; _bt.Status += s => Log("[BT] " + s);

            // 断线检测 → 自动重连（用户手动停止除外）
            _wifi.Status += s => { if (s.Contains("断开")) OnTransportDisconnected("WiFi"); };
            _usb.Status += s => { if (s.Contains("断开")) OnTransportDisconnected("USB"); };
            _bt.Status += s => { if (s.Contains("断开")) OnTransportDisconnected("BT"); };

            // 恢复上次的传输方式与端口
            var savedMode = GetSetting("Mode", "0");
            if (int.TryParse(savedMode, out int mi) && mi >= 0 && mi < _mode.Items.Count) _mode.SelectedIndex = mi;
            var savedPort = GetSetting("Port", "51000");
            if (System.Text.RegularExpressions.Regex.IsMatch(savedPort, @"^\d+$")) _port.Text = savedPort;
            _mode.SelectedIndexChanged += (s, e) => SetSetting("Mode", _mode.SelectedIndex.ToString());
            _port.TextChanged += (s, e) => { if (System.Text.RegularExpressions.Regex.IsMatch(_port.Text, @"^\d+$")) SetSetting("Port", _port.Text); };

            // 自动连接：启动后按上次设置自动点“启动”
            _autoReconnect = GetSetting("AutoReconnect", "1") == "1";
            if (GetSetting("AutoConnect", "0") == "1")
            {
                Log("[Auto] 自动连接已启用，正在启动…");
                _ = StartAsync();
            }
        }

        // 设置对话框：开机启动 + 自动连接
        private void ShowSettings()
        {
            using var dlg = new Form
            {
                Text = "设置",
                Size = new Size(360, 190),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false
            };
            var chkAutoStart = new CheckBox
            {
                Text = "开机自动启动",
                Left = 20, Top = 15, AutoSize = true,
                Checked = IsAutoStartEnabled()
            };
            var chkAutoConnect = new CheckBox
            {
                Text = "启动时自动连接（使用上次设置）",
                Left = 20, Top = 45, AutoSize = true,
                Checked = GetSetting("AutoConnect", "0") == "1"
            };
            var chkReconnect = new CheckBox
            {
                Text = "断线自动重连",
                Left = 20, Top = 75, AutoSize = true,
                Checked = GetSetting("AutoReconnect", "1") == "1"
            };
            var ok = new Button { Text = "确定", Left = 150, Top = 115, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Left = 235, Top = 115, Width = 75, DialogResult = DialogResult.Cancel };
            dlg.Controls.AddRange(new Control[] { chkAutoStart, chkAutoConnect, chkReconnect, ok, cancel });
            dlg.AcceptButton = ok; dlg.CancelButton = cancel;
            dlg.Size = new Size(360, 210);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                SetAutoStart(chkAutoStart.Checked);
                SetSetting("AutoConnect", chkAutoConnect.Checked ? "1" : "0");
                SetSetting("AutoReconnect", chkReconnect.Checked ? "1" : "0");
                _autoReconnect = chkReconnect.Checked;
            }
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "HcMicClient";
        private const string SettingsKey = @"Software\HcMic";

        // ── 设置持久化（注册表 HKCU\Software\HcMic）──
        private static string GetSetting(string name, string def = "")
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue(name) as string ?? def;
        }

        private static void SetSetting(string name, string value)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue(name, value);
        }

        private static bool IsAutoStartEnabled()
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) != null;
        }

        private static void SetAutoStart(bool enable)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
                key.SetValue(RunValue, $"\"{Application.ExecutablePath}\"");
            else
                key.DeleteValue(RunValue, false);
        }

        // ── 自动重连：连接断开后自动恢复监听/推流状态 ──
        private volatile bool _userStopped;   // 用户手动停止则不重连
        private bool _autoReconnect = true;   // 设置中的开关（默认开）

        private void OnTransportDisconnected(string tag)
        {
            if (_userStopped || !_autoReconnect) return;
            Log($"[{tag}] 检测到断开，{ReconnectDelaySeconds} 秒后自动重连…");
            _stat.Text = "断开，自动重连中…";
            var delay = Task.Delay(ReconnectDelaySeconds * 1000);
            delay.ContinueWith(_ =>
            {
                if (_userStopped || !_autoReconnect) return;
                BeginInvoke(async () =>
                {
                    // 先清理旧传输状态
                    try { _wifi.Stop(); _usb.Stop(); _bt.Stop(); } catch { }
                    await StartAsync();
                });
            });
        }

        private const int ReconnectDelaySeconds = 3;

        private static string FindAdb()
        {
            foreach (var p in new[] {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\Android\platform-tools\adb.exe",
                @"C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe" })
                if (System.IO.File.Exists(p)) return p;
            return "adb";
        }

        private async Task StartAsync()
        {
            try
            {
                int port = int.Parse(_port.Text);
                _btnStart.Enabled = false;
                switch (_mode.SelectedIndex)
                {
                    case 0: await _wifi.StartListenAsync(port); break;
                    case 1:
                        Log("[USB] 需要：1) 手机开启 USB 调试并连接；2) 手机端先点“USB 服务器”。");
                        await _usb.StartAsync(null, port, 51001); break;
                    case 2:
                        Log("[BT] 蓝牙模式需 32Feet 库与已配对设备，当前为占位实现。");
                        await _bt.StartServerAsync(); break;
                }
                _btnStop.Enabled = true;
                SetupAudioSink(); // 提前检测，连接建立前就显示输出方式
            }
            catch (Exception ex)
            {
                Log("[错误] " + ex.Message);
                _btnStart.Enabled = true;
            }
        }

        private void StopAll()
        {
            _userStopped = true;
            try { _wifi.Stop(); _usb.Stop(); _bt.Stop(); _sink?.Dispose(); _wasapi?.Dispose(); } catch { }
            _sink = null; _wasapi = null; _btnStart.Enabled = true; _btnStop.Enabled = false; _stat.Text = "已停止";
            // 推流结束，重新隐藏 CABLE Input 播放端点（保持纯净）
            EndpointMode("hide");
            // 稍后允许下次自动重连（重置标记）
            Task.Delay(1000).ContinueWith(_ => _userStopped = false);
        }

        // 建立音频输出：优先虚拟声卡（WASAPI 直写），否则回退命名管道
        // 推流期间确保 CABLE Input 端点可见（hide 过的端点无法被 WASAPI 打开），
        // 停止推流时恢复隐藏（保持"用户只见麦克风"的纯净体验）
        private void SetupAudioSink()
        {
            _sink?.Dispose(); _sink = null;
            _wasapi?.Dispose(); _wasapi = null;
            // 先确保端点可见
            EndpointMode("show");
            _wasapi = new WasapiAudioSink();
            if (_wasapi.Start(_fmt))
            {
                Log($"[Audio] 已连接虚拟声卡：{_wasapi.DeviceName}（其他软件中选对应的 Output 设备作麦克风）");
                return;
            }
            _wasapi.Dispose(); _wasapi = null;
            _sink = new PipeAudioSink();
            _sink.Start(_fmt);
            Log("[Audio] 未检测到虚拟声卡（VB-Cable），回退命名管道 \\\\.\\pipe\\HcMicAudio");
        }

        private void OnFrame(FrameType t, byte[] payload)
        {
            switch (t)
            {
                case FrameType.Hello:
                    if (payload.Length >= 11)
                    {
                        _fmt.ProtocolVersion = BitConverter.ToUInt32(payload, 0);
                        _fmt.SampleRate = BitConverter.ToUInt32(payload, 4);
                        _fmt.Channels = payload[8];
                        _fmt.BitsPerSample = BitConverter.ToUInt16(payload, 9);
                        Log($"[Hello] v{_fmt.ProtocolVersion} {_fmt.SampleRate}Hz {_fmt.Channels}ch {_fmt.BitsPerSample}bit");
                        SetupAudioSink();
                    }
                    break;
                case FrameType.Audio:
                    _bytes += payload.Length;
                    if (_wasapi != null) _wasapi.Feed(payload, 0, payload.Length);
                    else _sink?.Feed(payload, 0, payload.Length);
                    BeginInvoke((Action)(() => _stat.Text = $"推流中 {_bytes / 1024} KB"));
                    break;
                case FrameType.Ping: _wifi.Send(FrameType.Pong); _usb.Send(FrameType.Pong); break;
                case FrameType.Start: Log("[Start] 手机开始推流"); break;
                case FrameType.Stop: Log("[Stop] 手机停止推流"); break;
                case FrameType.Bye: Log("[Bye] 手机断开"); break;
            }
        }

        private void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => Log(s))); return; }
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
        }

        [STAThread]
        static void Main(string[] args)
        {
            // 命令行模式：EndpointTool 功能合并进来（hide/show/state 管理 CABLE Input 端点可见性）
            if (args.Length > 0)
            {
                var mode = args[0].ToLower();
                if (mode == "hide" || mode == "show" || mode == "state") { Environment.Exit(EndpointMode(mode)); }
            }
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }

        static int EndpointMode(string mode)
        {
            try
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                NAudio.CoreAudioApi.MMDevice target = null;
                foreach (var d in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.All))
                    if (d.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0) { target = d; break; }
                if (target == null) { Console.WriteLine("CABLE Input not found"); return 0; }
                var pc = (HcMic.Core.IPolicyConfig)new HcMic.Core.PolicyConfigClient();
                switch (mode)
                {
                    case "hide":
                        if (pc.SetEndpointVisibility(target.ID, 0) != 0) throw new Exception("hide failed");
                        Console.WriteLine("CABLE Input hidden");
                        break;
                    case "show":
                        if (pc.SetEndpointVisibility(target.ID, 1) != 0) throw new Exception("show failed");
                        Console.WriteLine("CABLE Input visible");
                        break;
                    default:
                        Console.WriteLine("state: " + target.State);
                        break;
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("Error: " + ex.Message); return 1; }
        }
    }
}

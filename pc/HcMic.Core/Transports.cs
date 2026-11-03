using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace HcMic.Core
{
    // WiFi 传输：PC 作为 TCP 服务器监听，手机连接后推送音频
    public class WifiTransport
    {
        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        public event Action<FrameType, byte[]> FrameReceived;
        public event Action<string> Status;
        public bool IsConnected => _client != null && _client.Connected;

        public async Task StartListenAsync(int port = 51000)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Status?.Invoke($"监听中 :{port}，等待手机连接…");
            _client = await _listener.AcceptTcpClientAsync();
            _stream = _client.GetStream();
            Status?.Invoke($"已连接：{((IPEndPoint)_client.Client.RemoteEndPoint).Address}");
            _ = Task.Run(() => ReadLoop());
        }

        private async void ReadLoop()
        {
            var reader = new FrameReader();
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    int n = await _stream.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var f = reader.TryRead(buf, n);
                    if (f.HasValue) FrameReceived?.Invoke(f.Value.type, f.Value.payload);
                }
            }
            catch { }
            Status?.Invoke("连接断开");
        }

        public void Send(FrameType type, byte[] payload = null)
        {
            if (_stream == null) return;
            try { var b = Protocol.Encode(type, payload); _stream.Write(b, 0, b.Length); _stream.Flush(); } catch { }
        }

        public void Stop()
        {
            try { Send(FrameType.Bye); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
            _client = null; _stream = null; _listener = null;
        }
    }

    // USB 传输：通过 adb forward 把 PC 端口转发到手机（手机本地起 TCP 服务器）
    // 这里 PC 侧仍为 TCP 客户端，连接 127.0.0.1:pcPort，数据经 USB 转发到手机端口
    public class UsbTransport
    {
        private TcpClient _client;
        private NetworkStream _stream;
        public event Action<FrameType, byte[]> FrameReceived;
        public event Action<string> Status;
        public string AdbPath = "adb";

        public async Task StartAsync(string deviceId, int pcPort, int phonePort = 51001)
        {
            var psi = new ProcessStartInfo(AdbPath, deviceId == null ? $"forward tcp:{pcPort} tcp:{phonePort}" : $"-s {deviceId} forward tcp:{pcPort} tcp:{phonePort}")
            { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            var p = Process.Start(psi);
            p.WaitForExit(5000);
            if (p.ExitCode != 0) throw new Exception("adb forward 失败：" + p.StandardError.ReadToEnd());

            _client = new TcpClient();
            await _client.ConnectAsync(IPAddress.Loopback, pcPort);
            _stream = _client.GetStream();
            Status?.Invoke($"USB 连接成功（经 adb forward {pcPort}→{phonePort}）");
            _ = Task.Run(() => ReadLoop());
        }

        private async void ReadLoop()
        {
            var reader = new FrameReader();
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    int n = await _stream.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var f = reader.TryRead(buf, n);
                    if (f.HasValue) FrameReceived?.Invoke(f.Value.type, f.Value.payload);
                }
            }
            catch { }
            Status?.Invoke("USB 连接断开");
        }

        public void Send(FrameType type, byte[] payload = null)
        {
            if (_stream == null) return;
            try { var b = Protocol.Encode(type, payload); _stream.Write(b, 0, b.Length); _stream.Flush(); } catch { }
        }

        public void Stop()
        {
            try { Send(FrameType.Bye); } catch { }
            try { _client?.Close(); } catch { }
            _client = null; _stream = null;
        }
    }

    // 蓝牙传输：PC 作为 RFCOMM 服务器（SPP），手机作为客户端连接
    // 使用 32Feet.NET (InTheHand.Net.Bluetooth)，UUID 与 Android 端一致
    public class BluetoothTransport
    {
        public event Action<FrameType, byte[]> FrameReceived;
        public event Action<string> Status;
        public bool IsConnected { get; private set; }
        private Stream _stream;
        private readonly Guid ServiceUuid = new Guid("8e5c4a00-1f2b-4c3d-9a6e-d5b11c000001");
        private BluetoothListener _listener;

        public async Task StartServerAsync()
        {
            Status?.Invoke("蓝牙：正在注册 SPP 服务（UUID 匹配手机端），等待手机连接…");
            await Task.Run(() =>
            {
                _listener = new BluetoothListener(ServiceUuid);
                _listener.Start();
                var client = _listener.AcceptBluetoothClient();
                _stream = client.GetStream();
                IsConnected = true;
                Status?.Invoke($"蓝牙已连接：{client.RemoteMachineName ?? "手机"}");
                ReadLoop();
            });
        }

        private async void ReadLoop()
        {
            var reader = new FrameReader();
            var buf = new byte[8192];
            try
            {
                while (true)
                {
                    int n = await _stream.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var f = reader.TryRead(buf, n);
                    if (f.HasValue) FrameReceived?.Invoke(f.Value.type, f.Value.payload);
                }
            }
            catch { }
            IsConnected = false;
            Status?.Invoke("蓝牙连接断开");
        }

        public void Send(FrameType type, byte[] payload = null)
        {
            if (_stream == null) return;
            try { var b = Protocol.Encode(type, payload); _stream.Write(b, 0, b.Length); _stream.Flush(); } catch { }
        }

        public void Stop()
        {
            try { Send(FrameType.Bye); } catch { }
            try { _stream?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
            IsConnected = false; _stream = null; _listener = null;
        }
    }
}

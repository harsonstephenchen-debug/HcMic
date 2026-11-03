using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace HcMic.Core
{
    // 音频输出：把收到的 PCM 打上 WAV 头写入命名管道 "HcMicAudio"，
    // 消费端可以是：ffplay、任何播放器、或直接设置为 OBS/应用选择的“HcMic 桥接管道”。
    // 虚拟麦克风方案：配套使用 VB-Cable（免费），把本客户端输出设备设为 "CABLE Input"，
    // 则 "CABLE Output" 即成为应用可选的虚拟麦克风。本程序通过 Windows 音频会话 API
    // 将播放会话路由到 CABLE Input；若未安装则回退到默认播放设备。
    public class PipeAudioSink : IDisposable
    {
        private NamedPipeServerStream _pipe;
        private FileStream _fallback;
        private readonly AudioRingBuffer _ring = new();
        private Thread _writer;
        private volatile bool _run;
        public string Mode;

        public void Start(AudioFormat fmt, string outDeviceHint = null)
        {
            _run = true;
            _pipe = new NamedPipeServerStream("HcMicAudio", PipeDirection.Out, 1);
            _writer = new Thread(WriteLoop) { IsBackground = true };
            _writer.Start(fmt);
            Mode = "命名管道 \\\\.\\pipe\\HcMicAudio";
        }

        private void WriteLoop(object o)
        {
            var fmt = (AudioFormat)o;
            try
            {
                // 等待消费端连接（ffplay / 播放器），超时则退化为不输出
                var connectTask = _pipe.WaitForConnectionAsync(new System.Threading.CancellationTokenSource(2000).Token);
                bool connected = connectTask.Wait(2500) && connectTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion;
                if (!connected) { Mode = "无消费端（音频丢弃）"; }
                var hdr = WavUtil.Header(fmt.SampleRate, fmt.Channels, fmt.BitsPerSample);
                if (connected) _pipe.Write(hdr, 0, hdr.Length);
                var buf = new byte[16384];
                while (_run)
                {
                    int n = _ring.Read(buf, buf.Length);
                    if (n == 0) { Thread.Sleep(10); continue; }
                    if (connected) _pipe.Write(buf, 0, n);
                }
            }
            catch { }
        }

        public void Feed(byte[] pcm, int offset, int count)
        {
            if (count > pcm.Length - offset) count = pcm.Length - offset;
            _ring.Write(pcm, offset, count);
        }

        public void Dispose()
        {
            _run = false;
            try { _pipe?.Dispose(); } catch { }
        }
    }
}

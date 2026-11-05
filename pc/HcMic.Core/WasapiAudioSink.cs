using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HcMic.Core
{
    // WASAPI 输出：自动检测虚拟声卡（VB-Cable "CABLE Input"、Scream 等），
    // 找到则直接把收到的 PCM 播放到该设备（应用即可把对应 "CABLE Output" 选为麦克风）；
    // 未找到则回退到命名管道模式。
    public class WasapiAudioSink : IDisposable
    {
        private IWavePlayer _output;
        private BufferedWaveProvider _buffer;
        public string DeviceName { get; private set; }
        public bool UsingVirtualDevice { get; private set; }

        private static readonly string[] VirtualNames = { "CABLE Input", "VB-Audio Virtual Cable", "Scream (Virtual Audio Cable)", "Virtual Audio Cable" };

        public static MMDevice FindVirtualDevice()
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                foreach (var name in VirtualNames)
                    if (d.FriendlyName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return d;
            }
            return null;
        }

        public bool Start(AudioFormat fmt)
        {
            try
            {
                var device = FindVirtualDevice();
                if (device == null) return false;
                var waveFormat = new WaveFormat((int)fmt.SampleRate, fmt.BitsPerSample, fmt.Channels);
                _buffer = new BufferedWaveProvider(waveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(500),   // 小缓冲防累积
                    DiscardOnBufferOverflow = true
                };
                // 低延迟 WASAPI：20ms 播放周期
                _output = new WasapiOut(device, AudioClientShareMode.Shared, false, 20);
                _output.Init(_buffer);
                _output.Play();
                DeviceName = device.FriendlyName;
                UsingVirtualDevice = true;
                return true;
            }
            catch { return false; }
        }

        public void Feed(byte[] pcm, int offset, int count)
        {
            if (count > pcm.Length - offset) count = pcm.Length - offset;
            _buffer?.AddSamples(pcm, offset, count);
            // 追赶逻辑：缓冲超过 200ms 说明网络/采集快于播放，丢弃多余部分防止延迟持续累积
            if (_buffer != null && _buffer.BufferedDuration > TimeSpan.FromMilliseconds(200))
            {
                var excess = _buffer.BufferedDuration - TimeSpan.FromMilliseconds(200);
                _buffer.ClearBuffer(); // 简单粗暴：清空重来（丢 ~1 帧以内可接受，保持低延迟优先）
            }
        }

        public void Dispose()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null; _buffer = null;
        }
    }
}

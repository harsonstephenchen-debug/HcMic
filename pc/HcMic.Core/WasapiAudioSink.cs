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

        private static readonly string[] VirtualNames = { "CABLE Input", "VB-Audio Virtual Cable", "Scream (Virtual Audio Cable)", "Virtual Audio Cable", "WO Mic Device" };

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
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true
                };
                _output = new WasapiOut(device, AudioClientShareMode.Shared, false, 40);
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
        }

        public void Dispose()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            _output = null; _buffer = null;
        }
    }
}

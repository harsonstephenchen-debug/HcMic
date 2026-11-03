using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace HcMic.Core
{
    // WAV 播放器：把收到的 PCM 写入命名管道，由 ffplay/播放端消费；
    // 若检测到 "CABLE Input"（VB-Cable）等虚拟设备则直接用 WASAPI 输出到该设备。
    // 简化方案：先用 NAudio 式 WASAPI 通过系统默认播放设备，后续可换。
    // 这里实现为：把 PCM 写入环形缓冲，由 AudioOut 抽象消费。
    public class AudioRingBuffer
    {
        private readonly byte[] _buf;
        private int _head, _tail; // 生产者写 _tail，消费者读 _head
        private readonly object _lock = new();

        public AudioRingBuffer(int capacity = 1 << 20) { _buf = new byte[capacity]; }

        public void Write(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    _buf[_tail] = data[offset + i];
                    _tail = (_tail + 1) % _buf.Length;
                    if (_tail == _head) _head = (_head + 1) % _buf.Length; // 丢弃最旧
                }
            }
        }

        public int Read(byte[] dst, int count)
        {
            lock (_lock)
            {
                int avail = (_tail - _head + _buf.Length) % _buf.Length;
                int n = Math.Min(avail, count);
                for (int i = 0; i < n; i++) { dst[i] = _buf[_head]; _head = (_head + 1) % _buf.Length; }
                return n;
            }
        }

        public int Available { get { lock (_lock) return (_tail - _head + _buf.Length) % _buf.Length; } }
    }

    // WAV 文件头计算工具：用于把 PCM 流包成 WAV（写到管道或文件）
    public static class WavUtil
    {
        public static byte[] Header(uint sampleRate, ushort channels, ushort bitsPerSample, uint dataBytes = 0xFFFFFFFF)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            ushort blockAlign = (ushort)(channels * bitsPerSample / 8);
            uint byteRate = sampleRate * blockAlign;
            w.Write("RIFF".ToCharArray());
            w.Write(dataBytes == 0xFFFFFFFF ? 0xFFFFFFFFu : 36 + dataBytes);
            w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray());
            w.Write(16u);
            w.Write((ushort)1); // PCM
            w.Write(channels);
            w.Write(sampleRate);
            w.Write(byteRate);
            w.Write(blockAlign);
            w.Write(bitsPerSample);
            w.Write("data".ToCharArray());
            w.Write(dataBytes);
            w.Flush();
            return ms.ToArray();
        }
    }
}

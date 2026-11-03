// HcMic 协议定义：PC 端与 Android 端共享的线上格式
//
// 帧格式（小端）：
//   [u8 magic=0x44 'D'][u8 type][u16 payloadLen][payload]
// type:
//   0x01 HELLO  payload: 8 字节协议版本 (u32) + 4 字节采样率 (u32) + u8声道数 + u16 位深/8
//   0x02 AUDIO  payload: 原始 PCM (16-bit LE)
//   0x03 PING   payload: 空（PC 回 PONG 保活）
//   0x04 PONG   payload: 空
//   0x05 BYE    payload: 空（优雅断开）
//   0x06 START  payload: 空（手机开始推流）
//   0x07 STOP   payload: 空（手机停止推流）
namespace HcMic.Core
{
    using System;
    using System.IO;

    public enum FrameType : byte
    {
        Hello = 0x01,
        Audio = 0x02,
        Ping = 0x03,
        Pong = 0x04,
        Bye = 0x05,
        Start = 0x06,
        Stop = 0x07,
    }

    public class AudioFormat
    {
        public uint ProtocolVersion;
        public uint SampleRate;
        public byte Channels;
        public ushort BitsPerSample;
    }

    public static class Protocol
    {
        public const byte Magic = 0x44;
        public const uint Version = 1;

        public static byte[] Encode(FrameType type, byte[] payload)
        {
            int len = payload?.Length ?? 0;
            var buf = new byte[4 + len];
            buf[0] = Magic;
            buf[1] = (byte)type;
            buf[2] = (byte)(len & 0xFF);
            buf[3] = (byte)((len >> 8) & 0xFF);
            if (len > 0) payload.CopyTo(buf, 4);
            return buf;
        }

        public static byte[] EncodeHello(AudioFormat f)
        {
            var p = new byte[11];
            WriteU32(p, 0, f.ProtocolVersion);
            WriteU32(p, 4, f.SampleRate);
            p[8] = f.Channels;
            WriteU16(p, 9, f.BitsPerSample);
            return Encode(FrameType.Hello, p);
        }

        private static void WriteU32(byte[] b, int off, uint v)
        {
            b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24);
        }

        private static void WriteU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)v; b[off + 1] = (byte)(v >> 8);
        }
    }

    // 增量帧读取器：处理 TCP 粘包/半包
    public class FrameReader
    {
        private readonly byte[] _buf = new byte[65536];
        private int _len;
        private readonly MemoryStream _payload = new();

        // 返回 null 表示数据不足
        public (FrameType type, byte[] payload)? TryRead(byte[] data, int count)
        {
            if (_len + count > _buf.Length) throw new IOException("frame overflow");
            Buffer.BlockCopy(data, 0, _buf, _len, count);
            _len += count;
            if (_len < 4) return null;
            if (_buf[0] != Protocol.Magic) { _len = 0; throw new IOException("bad magic"); }
            int plen = _buf[2] | (_buf[3] << 8);
            if (_len < 4 + plen) return null;
            var type = (FrameType)_buf[1];
            var payload = new byte[plen];
            Buffer.BlockCopy(_buf, 4, payload, 0, plen);
            int rest = _len - 4 - plen;
            if (rest > 0) Buffer.BlockCopy(_buf, 4 + plen, _buf, 0, rest);
            _len = rest;
            return (type, payload);
        }
    }
}

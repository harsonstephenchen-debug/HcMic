package com.hcmic.app

import java.io.InputStream
import java.io.OutputStream
import java.net.ServerSocket
import java.net.Socket

// 协议帧：[0x44][type][len_lo][len_hi][payload]
object Protocol {
    const val MAGIC = 0x44
    const val VERSION = 1L

    const val TYPE_HELLO = 0x01
    const val TYPE_AUDIO = 0x02
    const val TYPE_PING = 0x03
    const val TYPE_PONG = 0x04
    const val TYPE_BYE = 0x05
    const val TYPE_START = 0x06
    const val TYPE_STOP = 0x07

    fun hello(sampleRate: Int, channels: Int, bits: Int): ByteArray {
        val p = ByteArray(11)
        putU32(p, 0, VERSION)
        putU32(p, 4, sampleRate.toLong())
        p[8] = channels.toByte()
        putU16(p, 9, bits)
        return frame(TYPE_HELLO, p)
    }

    fun audio(pcm: ByteArray, len: Int): ByteArray = frame(TYPE_AUDIO, pcm.copyOf(len))

    fun frame(type: Int, payload: ByteArray = ByteArray(0)): ByteArray {
        val b = ByteArray(4 + payload.size)
        b[0] = MAGIC.toByte(); b[1] = type.toByte()
        b[2] = (payload.size and 0xFF).toByte(); b[3] = ((payload.size shr 8) and 0xFF).toByte()
        payload.copyInto(b, 4)
        return b
    }

    private fun putU32(b: ByteArray, o: Int, v: Long) {
        b[o] = v.toByte(); b[o+1] = (v shr 8).toByte(); b[o+2] = (v shr 16).toByte(); b[o+3] = (v shr 24).toByte()
    }
    private fun putU16(b: ByteArray, o: Int, v: Int) { b[o] = v.toByte(); b[o+1] = (v shr 8).toByte() }

    fun readFrame(input: InputStream): Pair<Int, ByteArray>? {
        val hdr = ByteArray(4)
        if (!readFully(input, hdr)) return null
        if (hdr[0].toInt() and 0xFF != MAGIC) return null
        val type = hdr[1].toInt() and 0xFF
        val len = (hdr[2].toInt() and 0xFF) or ((hdr[3].toInt() and 0xFF) shl 8)
        val payload = ByteArray(len)
        if (len > 0 && !readFully(input, payload)) return null
        return type to payload
    }

    private fun readFully(input: InputStream, b: ByteArray): Boolean {
        var off = 0
        while (off < b.size) {
            val n = input.read(b, off, b.size - off)
            if (n <= 0) return false
            off += n
        }
        return true
    }
}

// WiFi 传输（手机端为 TCP 服务器，PC 直连或经 adb forward 连接）
class WifiTransport(private val port: Int = 51001) : Transport {
    private var server: ServerSocket? = null
    private var socket: Socket? = null
    override var input: InputStream? = null
    override var output: OutputStream? = null
    override var onFrame: ((Int, ByteArray) -> Unit)? = null
    override var onStatus: ((String) -> Unit)? = null

    override fun start() {
        server = ServerSocket(port)
        onStatus?.invoke("WiFi 服务器监听 :$port，等待电脑连接…")
        val s = server!!.accept()
        s.tcpNoDelay = true
        socket = s
        input = s.getInputStream(); output = s.getOutputStream()
        onStatus?.invoke("已连接：${s.inetAddress?.hostAddress}")
    }

    override fun send(type: Int, payload: ByteArray) {
        try { output?.write(Protocol.frame(type, payload)); output?.flush() } catch (_: Exception) {}
    }

    override fun stop() {
        try { send(Protocol.TYPE_BYE) } catch (_: Exception) {}
        try { socket?.close() } catch (_: Exception) {}
        try { server?.close() } catch (_: Exception) {}
        input = null; output = null
    }
}

package com.hcmic.app

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat

// 前台服务：采集麦克风 PCM 并经当前 Transport 推流；断线自动重连
class MicService : Service() {
    companion object {
        const val CHANNEL_ID = "hcmic"
        const val NOTIF_ID = 1
        const val MAX_RETRIES = 5
        const val RETRY_DELAY_MS = 2000L
        @Volatile var isStreaming = false
        @Volatile var currentTransport: Transport? = null
        @Volatile var autoReconnect = true   // 与设置界面对应
        var sampleRate = 48000
        var channels = 1
        var bitsPerSample = 16
        var blockMs = 20   // 采集块时长（延迟档位：低=20 中=50 高=100）
        var reconnectFn: (() -> Unit)? = null  // 由 MainActivity 注入：重开传输的函数
    }

    private var record: AudioRecord? = null
    private var streamThread: Thread? = null
    private var retries = 0

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(NOTIF_ID, buildNotification("HcMic 运行中"))
        retries = 0
        if (!isStreaming) startStreaming()
        return START_STICKY
    }

    override fun onDestroy() {
        autoReconnect = false
        stopStreaming()
        super.onDestroy()
    }

    private fun buildNotification(text: String): Notification {
        if (Build.VERSION.SDK_INT >= 26) {
            val ch = NotificationChannel(CHANNEL_ID, "HcMic", NotificationManager.IMPORTANCE_LOW)
            getSystemService(NotificationManager::class.java).createNotificationChannel(ch)
        }
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("HcMic").setContentText(text).setSmallIcon(android.R.drawable.ic_btn_speak_now)
            .setOngoing(true).build()
    }

    private fun startStreaming() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            stopSelf(); return
        }
        val chMask = if (channels == 2) AudioFormat.CHANNEL_IN_STEREO else AudioFormat.CHANNEL_IN_MONO
        // 块大小由延迟档位决定（blockMs），AudioRecord 缓冲压到 4 块，减少系统攒数据延迟
        val blockBytes = sampleRate * blockMs / 1000 * 2 * channels
        val minBuf = AudioRecord.getMinBufferSize(sampleRate, chMask, AudioFormat.ENCODING_PCM_16BIT)
        record = AudioRecord(MediaRecorder.AudioSource.MIC, sampleRate, chMask, AudioFormat.ENCODING_PCM_16BIT, maxOf(minBuf, blockBytes * 4))
        isStreaming = true
        streamThread = Thread {
            val buf = ByteArray(blockBytes)
            // Android 音源默认带 ~20-100ms 处理延迟，VOICE_COMMUNICATION 会启用回声消除并降延迟；MIC 保真优先。
            // 用户场景是当麦克风（对方要听），用 VOICE_COMMUNICATION 换更低延迟更合适。
            record?.startRecording()
            currentTransport?.send(Protocol.TYPE_HELLO) ?: return@Thread
            currentTransport?.send(Protocol.TYPE_START)
            while (isStreaming) {
                val n = record?.read(buf, 0, buf.size) ?: -1
                if (n > 0) {
                    val t = currentTransport
                    if (t == null || !t.isConnected) break
                    try { t.send(Protocol.TYPE_AUDIO, buf.copyOf(n)) } catch (_: Exception) { break }
                }
            }
            currentTransport?.send(Protocol.TYPE_STOP)
            record?.stop(); record?.release(); record = null
            // 推流循环退出 = 连接断开 → 自动重连
            handleDisconnect()
        }.also { it.start() }
    }

    private fun handleDisconnect() {
        if (!isStreaming) return  // 用户手动停止
        if (!autoReconnect || retries >= MAX_RETRIES) {
            isStreaming = false
            updateNotification("HcMic 已停止（重连失败 $retries 次）")
            return
        }
        retries++
        updateNotification("HcMic 连接断开，$RETRY_DELAY_MS/1000*${retries}s 后自动重连（$retries/$MAX_RETRIES）")
        Thread {
            Thread.sleep(RETRY_DELAY_MS * retries)
            if (!isStreaming || !autoReconnect) return@Thread
            try {
                // 重建传输并重启推流
                reconnectFn?.invoke()
            } catch (_: Exception) {
                handleDisconnect()
            }
        }.start()
    }

    private fun updateNotification(text: String) {
        val nm = getSystemService(NotificationManager::class.java)
        nm.notify(NOTIF_ID, buildNotification(text))
    }

    private fun stopStreaming() {
        isStreaming = false
        try { streamThread?.join(1000) } catch (_: Exception) {}
        streamThread = null
    }
}

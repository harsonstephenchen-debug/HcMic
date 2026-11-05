package com.hcmic.app

import android.Manifest
import android.bluetooth.BluetoothAdapter
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.RadioGroup
import android.widget.Spinner
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID
import kotlin.concurrent.thread

interface Transport {
    var input: InputStream?
    var output: OutputStream?
    var onFrame: ((Int, ByteArray) -> Unit)?
    var onStatus: ((String) -> Unit)?
    val isConnected: Boolean get() = output != null
    fun start()
    fun send(type: Int, payload: ByteArray = ByteArray(0))
    fun stop()
}

// 蓝牙 SPP 传输：手机作为客户端连 PC（PC 端需注册同 UUID 的 RFCOMM 服务）
class BluetoothTransport : Transport {
    override var input: InputStream? = null
    override var output: OutputStream? = null
    override var onFrame: ((Int, ByteArray) -> Unit)? = null
    override var onStatus: ((String) -> Unit)? = null
    private val uuid: UUID = UUID.fromString("8e5c4a00-1f2b-4c3d-9a6e-d5b11c000001")

    override fun start() {
        val adapter = BluetoothAdapter.getDefaultAdapter() ?: run { onStatus?.invoke("无蓝牙"); return }
        if (!adapter.isEnabled) { onStatus?.invoke("蓝牙未开启"); return }
        val pc = adapter.bondedDevices.firstOrNull() ?: run { onStatus?.invoke("无已配对设备"); return }
        onStatus?.invoke("连接已配对设备 ${pc.name} …")
        var lastError: Exception? = null
        // Android 蓝牙连接常见瞬时失败，重试 3 次并做 fallback 通道
        for (attempt in 1..3) {
            val sock = try {
                if (attempt < 3) pc.createRfcommSocketToServiceRecord(uuid)
                else {
                    try { pc.javaClass.getMethod("createRfcommSocket", Int::class.javaPrimitiveType).invoke(pc, 1) as android.bluetooth.BluetoothSocket }
                    catch (_: Exception) { pc.createRfcommSocketToServiceRecord(uuid) }
                }
            } catch (e: Exception) { lastError = e; continue }
            try {
                sock.connect()
                input = sock.inputStream; output = sock.outputStream
                onStatus?.invoke("蓝牙已连接 ${pc.name}")
                return
            } catch (e: Exception) {
                lastError = e
                try { sock.close() } catch (_: Exception) {}
                onStatus?.invoke("蓝牙第 $attempt 次尝试失败，重试…")
                Thread.sleep(500)
            }
        }
        onStatus?.invoke("蓝牙连接失败：${lastError?.message ?: "未知错误"}（请确认电脑端 HcMic 已选蓝牙并启动，且设备已配对）")
    }
    override fun send(type: Int, payload: ByteArray) {
        try { output?.write(Protocol.frame(type, payload)); output?.flush() } catch (_: Exception) {}
    }
    override fun stop() { try { send(Protocol.TYPE_BYE) } catch (_: Exception) {} ; try { output?.close() } catch (_: Exception) {}; input = null; output = null }
}

class MainActivity : AppCompatActivity() {
    private lateinit var transport: Transport

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        if (Build.VERSION.SDK_INT >= 33) {
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO, Manifest.permission.POST_NOTIFICATIONS), 1)
        } else {
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), 1)
        }

        val modeGroup = findViewById<RadioGroup>(R.id.modeGroup)
        val addrEdit = findViewById<EditText>(R.id.pcAddress)
        val portEdit = findViewById<EditText>(R.id.port)
        val srSpinner = findViewById<Spinner>(R.id.sampleRate)
        val chSpinner = findViewById<Spinner>(R.id.channels)
        val latencySpinner = findViewById<Spinner>(R.id.latency)
        val btnStart = findViewById<Button>(R.id.btnStart)
        val btnStop = findViewById<Button>(R.id.btnStop)

        btnStart.setOnClickListener {
            if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                Toast.makeText(this, "需要麦克风权限", Toast.LENGTH_SHORT).show(); return@setOnClickListener
            }
            // Android 12+：蓝牙连接前必须动态申请 BLUETOOTH_CONNECT
            if (Build.VERSION.SDK_INT >= 31 &&
                checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT), 2)
                Toast.makeText(this, "请先授予“附近的设备”权限", Toast.LENGTH_LONG).show()
                return@setOnClickListener
            }
            MicService.sampleRate = srSpinner.selectedItem.toString().toInt()
            MicService.channels = chSpinner.selectedItemPosition + 1
            MicService.bitsPerSample = 16
            // 延迟档位真正接入：低=20ms 块，中=50ms，高=100ms
            MicService.blockMs = when (latencySpinner.selectedItemPosition) { 0 -> 20; 1 -> 50; else -> 100 }
            MicService.autoReconnect = true
            MicService.reconnectFn = { startTransport(modeGroup, portEdit) }
            startTransport(modeGroup, portEdit)
            btnStart.isEnabled = false; btnStop.isEnabled = true
        }

        btnStop.setOnClickListener {
            MicService.autoReconnect = false
            stopService(Intent(this, MicService::class.java))
            transport?.stop()
            btnStart.isEnabled = true; btnStop.isEnabled = false
        }
    }

    // 建立传输并启动推流服务（btnStart 与自动重连共用）
    private fun startTransport(modeGroup: RadioGroup, portEdit: EditText) {
        val t = when (modeGroup.checkedRadioButtonId) {
            R.id.modeWifi -> WifiTransport(portEdit.text.toString().toIntOrNull() ?: 51001)
            R.id.modeBt -> BluetoothTransport()
            else -> WifiTransport(51001) // USB 模式同样是本地 TCP（经 adb forward）
        }
        transport = t
        t.onStatus = { s -> runOnUiThread { Toast.makeText(this, s, Toast.LENGTH_SHORT).show() } }
        thread {
            try {
                t.start()
                MicService.currentTransport = t
                thread(name = "reader") {
                    try {
                        while (t.isConnected) {
                            val f = Protocol.readFrame(t.input ?: break) ?: break
                            if (f.first == Protocol.TYPE_PING) t.send(Protocol.TYPE_PONG)
                        }
                    } catch (_: Exception) {}
                }
                startForegroundService(Intent(this, MicService::class.java))
            } catch (e: Exception) {
                runOnUiThread { Toast.makeText(this, "连接失败：${e.message}", Toast.LENGTH_LONG).show() }
                // 连接建立失败也交给 MicService 的重连逻辑
                if (MicService.autoReconnect && MicService.isStreaming) throw e
            }
        }
    }
}

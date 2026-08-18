package com.bridge.keyboard

import android.os.Build
import android.os.Handler
import android.os.Looper
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

/**
 * 与 PC 端 Agent 的 WebSocket 连接管理：
 * 自动重连（指数退避）、状态订阅、等待 ack 的发送（用于发送校验）。
 */
object Conn {

    /** 连接状态 */
    sealed class State {
        data object Disconnected : State()
        data object Connecting : State()
        data object Connected : State() // 已通过 hello / auth_ok
    }

    /** PC 端焦点状态（由 PC 推送） */
    data class Focus(val ready: Boolean, val app: String, val field: String)

    /** 一次发送的结果 */
    data class SendResult(val ok: Boolean, val reason: String)

    private const val ACK_TIMEOUT_MS = 5000L

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val client = OkHttpClient.Builder()
        .pingInterval(10, TimeUnit.SECONDS)
        .connectTimeout(5, TimeUnit.SECONDS)
        .build()

    private val mainHandler = Handler(Looper.getMainLooper())
    private val listeners = mutableListOf<(State, Focus) -> Unit>()

    private val pending = ConcurrentHashMap<String, CompletableDeferred<String?>>()

    @Volatile private var ws: WebSocket? = null
    @Volatile private var state: State = State.Disconnected
    @Volatile private var focus = Focus(false, "", "")
    @Volatile private var pcName = ""
    @Volatile private var desired = false
    @Volatile private var url = ""
    @Volatile private var token = ""
    @Volatile private var backoffMs = 1000L
    private var reconnectJob: Job? = null

    fun addListener(l: (State, Focus) -> Unit) {
        listeners.add(l)
    }

    fun removeListener(l: (State, Focus) -> Unit) {
        listeners.remove(l)
    }

    fun getState(): State = state
    fun getFocus(): Focus = focus
    fun getPcName(): String = pcName

    fun configure(url: String, token: String) {
        this.url = url
        this.token = token
    }

    /** 启动连接（幂等；App 在前台时调用） */
    fun start() {
        desired = true
        if (ws == null) connect()
    }

    /** 停止连接（不再自动重连） */
    fun stop() {
        desired = false
        reconnectJob?.cancel()
        ws?.close(1000, "bye")
        ws = null
        state = State.Disconnected
        notifyListeners()
    }

    /** 配置变更后重连 */
    fun restart(url: String, token: String) {
        configure(url, token)
        ws?.close(1000, "restart")
        ws = null
        if (desired) connect() else notifyListeners()
    }

    private fun connect() {
        if (url.isBlank()) {
            state = State.Disconnected
            notifyListeners()
            return
        }
        state = State.Connecting
        notifyListeners()
        val request = Request.Builder().url(url).build()
        ws = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                val hello = JSONObject()
                    .put("type", "hello")
                    .put("token", token)
                    .put("device", Build.MODEL ?: "Android")
                webSocket.send(hello.toString())
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                handleJson(text)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                if (webSocket !== ws) return // 旧连接的回调，忽略
                onLinkDown()
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                if (webSocket !== ws) return
                onLinkDown()
            }
        })
    }

    private fun onLinkDown() {
        ws = null
        state = State.Disconnected
        failAllPending("disconnected")
        notifyListeners()
        scheduleReconnect()
    }

    private fun scheduleReconnect() {
        if (!desired) return
        if (reconnectJob?.isActive == true) return
        reconnectJob = scope.launch {
            delay(backoffMs)
            backoffMs = (backoffMs * 2).coerceAtMost(15000L)
            if (desired && ws == null) connect()
        }
    }

    private fun handleJson(text: String) {
        try {
            val o = JSONObject(text)
            when (o.optString("type")) {
                "auth_ok" -> {
                    pcName = o.optString("pc", "PC")
                    backoffMs = 1000L
                    state = State.Connected
                    notifyListeners()
                }
                "focus" -> {
                    focus = Focus(o.optBoolean("ready"), o.optString("app"), o.optString("field"))
                    notifyListeners()
                }
                "ack" -> pending.remove(o.optString("msgId"))?.complete("ack")
                "nack" -> pending.remove(o.optString("msgId"))?.complete("nack:" + o.optString("reason"))
                "pong" -> Unit
            }
        } catch (_: Exception) {
        }
    }

    /**
     * 发送整段文本到 PC。
     * verify=false：发出即认为成功（由调用方立即清空输入框）。
     * verify=true：等待 PC 注入成功的 ack（最长 5 秒）。
     */
    suspend fun sendText(text: String, overwrite: Boolean, verify: Boolean): SendResult {
        val sock = ws
        if (state !is State.Connected || sock == null) return SendResult(false, "not_connected")

        val msgId = UUID.randomUUID().toString().replace("-", "").substring(0, 12)
        val msg = JSONObject()
            .put("type", "text")
            .put("msgId", msgId)
            .put("text", text)
            .put("overwrite", overwrite)

        if (!verify) {
            return if (sock.send(msg.toString())) SendResult(true, "")
            else SendResult(false, "send_failed")
        }

        val deferred = CompletableDeferred<String?>()
        pending[msgId] = deferred
        try {
            if (!sock.send(msg.toString())) return SendResult(false, "send_failed")
            val res = withTimeoutOrNull(ACK_TIMEOUT_MS) { deferred.await() }
            return when {
                res == null -> SendResult(false, "timeout")
                res == "ack" -> SendResult(true, "")
                else -> SendResult(false, res!!.substringAfter(':').ifBlank { "nack" })
            }
        } finally {
            pending.remove(msgId)
        }
    }

    private fun failAllPending(reason: String) {
        pending.forEach { (k, v) -> v.complete("nack:$reason") }
        pending.clear()
    }

    private fun notifyListeners() {
        val s = state
        val f = focus
        mainHandler.post {
            listeners.forEach { it(s, f) }
        }
    }
}

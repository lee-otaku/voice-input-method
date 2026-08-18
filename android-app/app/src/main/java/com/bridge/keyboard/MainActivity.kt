package com.bridge.keyboard

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.Editable
import android.text.TextWatcher
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.bridge.keyboard.databinding.ActivityMainBinding
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val handler = Handler(Looper.getMainLooper())
    private var debounceRunnable: Runnable? = null
    private var sendJob: Job? = null
    private var listener: ((Conn.State, Conn.Focus) -> Unit)? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.btnSend.setOnClickListener { doSend(manual = true) }
        binding.btnSettings.setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }

        binding.input.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) = Unit
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) = Unit
            override fun afterTextChanged(s: Editable?) = onInputChanged()
        })

        listener = { _, _ -> runOnUiThread { renderStatus() } }
        Conn.addListener(listener!!)
    }

    override fun onResume() {
        super.onResume()
        val c = Prefs.load(this)
        Conn.configure(c.url, c.token)
        Conn.start()
        renderStatus()
        renderModeHint()
    }

    override fun onPause() {
        super.onPause()
        cancelDebounce()
    }

    override fun onDestroy() {
        super.onDestroy()
        listener?.let { Conn.removeListener(it) }
    }

    /** 输入内容变化：自动模式下重置去抖计时器 */
    private fun onInputChanged() {
        val c = Prefs.load(this)
        if (!c.autoMode) return
        if (sendJob?.isActive == true) return
        cancelDebounce()
        if (binding.input.text.isNotBlank()) {
            debounceRunnable = Runnable { doSend(manual = false) }
            handler.postDelayed(debounceRunnable!!, c.debounceSec * 1000L)
        }
    }

    private fun cancelDebounce() {
        debounceRunnable?.let { handler.removeCallbacks(it) }
        debounceRunnable = null
    }

    /**
     * 发送状态机（同一时刻只允许一个在途发送）：
     * 成功 → 清空输入框；失败/超时 → 保留内容并提示。
     */
    private fun doSend(manual: Boolean) {
        val text = binding.input.text.toString()
        if (text.isEmpty()) {
            if (manual) toast("没有可发送的内容")
            return
        }
        if (sendJob?.isActive == true) return
        cancelDebounce()

        val c = Prefs.load(this)
        sendJob = lifecycleScope.launch {
            binding.btnSend.isEnabled = false
            binding.statusSending.visibility = View.VISIBLE
            val r = withContext(Dispatchers.IO) { Conn.sendText(text, c.overwrite, c.verify) }
            binding.btnSend.isEnabled = true
            binding.statusSending.visibility = View.GONE

            if (r.ok) {
                binding.input.setText("")
                if (!manual) toast("已自动发送到电脑")
            } else {
                toast(when (r.reason) {
                    "not_connected" -> "未连接电脑，请先在设置中配对"
                    "no_focus" -> "电脑端没有可输入的焦点"
                    "not_enabled" -> "电脑端已停用输入映射"
                    "inject_failed" -> "电脑端注入失败，内容已保留"
                    "timeout" -> "等待电脑确认超时，内容已保留"
                    else -> "发送失败：${r.reason}"
                })
            }
        }
    }

    private fun renderStatus() {
        val state = Conn.getState()
        val focus = Conn.getFocus()
        binding.tvConn.text = when (state) {
            is Conn.State.Disconnected -> "未连接"
            is Conn.State.Connecting -> "连接中…"
            is Conn.State.Connected -> "已连接：${Conn.getPcName()}"
        }
        binding.tvConn.setTextColor(
            ContextCompat.getColor(
                this,
                if (state is Conn.State.Connected) R.color.ok_green else R.color.warn_red
            )
        )
        binding.tvFocus.text = if (focus.ready) {
            buildString {
                append("电脑端焦点：")
                append(focus.app.ifBlank { "可输入" })
                if (focus.field.isNotBlank()) append(" · ").append(focus.field)
            }
        } else {
            "电脑端无输入焦点"
        }
    }

    private fun renderModeHint() {
        val c = Prefs.load(this)
        binding.tvMode.text = if (c.autoMode) {
            "自动模式：停顿 ${c.debounceSec} 秒后自动发送"
        } else {
            "手动模式：点击按钮发送"
        }
    }

    private fun toast(msg: String) = Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
}

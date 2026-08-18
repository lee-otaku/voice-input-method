package com.bridge.keyboard

import android.content.ClipboardManager
import android.os.Bundle
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.bridge.keyboard.databinding.ActivitySettingsBinding

class SettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivitySettingsBinding

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivitySettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val c = Prefs.load(this)
        binding.edUrl.setText(c.url)
        binding.edToken.setText(c.token)
        if (c.autoMode) binding.radioAuto.isChecked = true else binding.radioManual.isChecked = true
        binding.edDebounce.setText(c.debounceSec.toString())
        binding.swVerify.isChecked = c.verify
        binding.swOverwrite.isChecked = c.overwrite

        // 解析配对串：优先取输入框内容，为空则读剪贴板
        binding.btnApplyPairing.setOnClickListener {
            val field = binding.edPairing.text.toString().trim()
            val text = field.ifBlank { readClipboard() }
            applyPairing(text)
        }

        binding.btnSave.setOnClickListener { save() }
    }

    private fun readClipboard(): String = try {
        val cm = getSystemService(CLIPBOARD_SERVICE) as ClipboardManager
        cm.primaryClip?.getItemAt(0)?.text?.toString() ?: ""
    } catch (_: Exception) {
        ""
    }

    private fun applyPairing(text: String) {
        val parsed = Prefs.parsePairing(text)
        if (parsed == null) {
            toast("配对串无效（应以 ws:// 开头，从电脑端配对窗口复制）")
            return
        }
        binding.edUrl.setText(parsed.first)
        binding.edToken.setText(parsed.second)
        toast("已解析配对串，点击「保存并连接」")
    }

    private fun save() {
        var url = binding.edUrl.text.toString().trim()
        val token = binding.edToken.text.toString().trim()

        if (url.isNotEmpty() && !url.startsWith("ws://") && !url.startsWith("wss://")) {
            if (url.contains("://")) {
                toast("地址需以 ws:// 开头")
                return
            }
            url = "ws://$url"
        }
        if (url.isNotEmpty() && !url.endsWith("/")) url += "/"
        if (url.isEmpty() || token.isEmpty()) {
            toast("请填写服务器地址与 Token")
            return
        }

        val debounce = binding.edDebounce.text.toString().toIntOrNull()?.coerceIn(1, 60) ?: 3
        val cfg = Prefs.Config(
            url = url,
            token = token,
            autoMode = binding.radioAuto.isChecked,
            debounceSec = debounce,
            verify = binding.swVerify.isChecked,
            overwrite = binding.swOverwrite.isChecked
        )
        Prefs.save(this, cfg)
        Conn.restart(cfg.url, cfg.token)
        toast("已保存")
        finish()
    }

    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()
}

package com.bridge.keyboard

import android.content.Context

/** 配置持久化（SharedPreferences） */
object Prefs {

    data class Config(
        val url: String,        // ws://ip:port/
        val token: String,      // 配对 Token
        val autoMode: Boolean,  // true=自动模式（停顿后发送）
        val debounceSec: Int,   // 自动模式停顿秒数
        val verify: Boolean,    // 发送校验（收到电脑 ack 后才清空输入框）
        val overwrite: Boolean  // 覆盖模式（发送前清空电脑输入框原有内容）
    )

    private const val FILE = "config"

    fun load(ctx: Context): Config {
        val sp = ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE)
        return Config(
            url = sp.getString("url", "") ?: "",
            token = sp.getString("token", "") ?: "",
            autoMode = sp.getBoolean("autoMode", true),
            debounceSec = sp.getInt("debounceSec", 3),
            verify = sp.getBoolean("verify", true),
            overwrite = sp.getBoolean("overwrite", false)
        )
    }

    fun save(ctx: Context, c: Config) {
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit()
            .putString("url", c.url.trim())
            .putString("token", c.token.trim())
            .putBoolean("autoMode", c.autoMode)
            .putInt("debounceSec", c.debounceSec)
            .putBoolean("verify", c.verify)
            .putBoolean("overwrite", c.overwrite)
            .apply()
    }

    /** 解析整段配对串 ws://ip:port/?token=xxx → (url, token)；失败返回 null */
    fun parsePairing(s: String): Pair<String, String>? {
        val t = s.trim()
        if (!t.startsWith("ws://") && !t.startsWith("wss://")) return null
        val token = Regex("[?&]token=([^&\\s]+)").find(t)?.groupValues?.get(1) ?: ""
        val base = t.substringBefore('?')
        return base to token
    }
}

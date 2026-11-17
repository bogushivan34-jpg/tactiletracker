package com.tactile.issues

// Warm felt-mallet / marimba UI sounds: same synth as src/lib/tactileSound.ts and windows/src/Sound.cs.
// Rendered once to small WAVs in the cache dir (off the main thread), played via SoundPool.

import android.content.Context
import android.media.AudioAttributes
import android.media.SoundPool
import java.io.DataOutputStream
import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.concurrent.thread
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.exp
import kotlin.math.ln
import kotlin.math.max
import kotlin.math.sin
import kotlin.math.tanh

object Sound {
    @Volatile var enabled = true
    private var pool: SoundPool? = null
    private val ids = HashMap<String, Int>()

    private class Note(val f: Double, val delay: Double, val dur: Double, val vol: Double, val cutoff: Double)

    private val defs = mapOf(
        "completed" to listOf(Note(587.33, 0.0, 0.22, 0.25, 1500.0), Note(880.0, 0.055, 0.28, 0.28, 1650.0)),
        "in_work" to listOf(Note(587.33, 0.0, 0.16, 0.21, 1350.0), Note(739.99, 0.045, 0.18, 0.22, 1400.0), Note(880.0, 0.09, 0.22, 0.24, 1450.0)),
        "backlog" to listOf(Note(440.0, 0.0, 0.15, 0.17, 950.0), Note(329.63, 0.04, 0.18, 0.16, 820.0)),
        "task" to listOf(Note(329.63, 0.0, 0.14, 0.19, 1100.0), Note(392.0, 0.035, 0.16, 0.20, 1150.0)),
        "expand" to listOf(Note(369.99, 0.0, 0.16, 0.20, 1100.0), Note(440.0, 0.035, 0.20, 0.22, 1200.0)),
        "collapse" to listOf(Note(440.0, 0.0, 0.14, 0.18, 1050.0), Note(369.99, 0.03, 0.16, 0.17, 950.0)),
        "add" to listOf(Note(523.25, 0.0, 0.18, 0.22, 1350.0), Note(659.25, 0.045, 0.24, 0.24, 1450.0)),
        "drag_start" to listOf(Note(220.0, 0.0, 0.12, 0.18, 750.0)),
        "drop" to listOf(Note(293.66, 0.0, 0.15, 0.22, 950.0)),
    )

    fun init(ctx: Context) {
        enabled = ctx.getSharedPreferences("prefs", Context.MODE_PRIVATE).getBoolean("sound", true)
        thread(name = "sound-init", priority = Thread.MIN_PRIORITY) {
            val p = SoundPool.Builder().setMaxStreams(4).setAudioAttributes(
                AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                    .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build()
            ).build()
            val dir = File(ctx.cacheDir, "sounds-v1").apply { mkdirs() }
            for ((name, notes) in defs) {
                val f = File(dir, "$name.wav")
                if (!f.exists()) writeWav(f, render(notes))
                synchronized(ids) { ids[name] = p.load(f.path, 1) }
            }
            pool = p
        }
    }

    fun setEnabled(ctx: Context, on: Boolean) {
        enabled = on
        ctx.getSharedPreferences("prefs", Context.MODE_PRIVATE).edit().putBoolean("sound", on).apply()
    }

    fun play(name: String) {
        if (!enabled) return
        val p = pool ?: return
        val id = synchronized(ids) { ids[name] } ?: return
        p.play(id, 1f, 1f, 1, 0, 1f)
    }

    fun status(s: com.tactile.issues.model.Status) = play(
        when (s) {
            com.tactile.issues.model.Status.COMPLETED -> "completed"
            com.tactile.issues.model.Status.IN_WORK -> "in_work"
            com.tactile.issues.model.Status.BACKLOG -> "backlog"
            com.tactile.issues.model.Status.TASK -> "task"
        }
    )

    fun expand(open: Boolean) = play(if (open) "expand" else "collapse")

    private const val RATE = 44100

    private fun render(notes: List<Note>): ShortArray {
        val total = notes.maxOf { it.delay + it.dur + 0.03 }
        val n = (total * RATE).toInt()
        val mix = DoubleArray(n)
        for (note in notes) {
            val start = (note.delay * RATE).toInt()
            val len = ((note.dur + 0.02) * RATE).toInt()
            val w0 = 2 * PI * note.cutoff / RATE
            val alpha = sin(w0) / (2 * 0.7)
            val a0 = 1 + alpha
            val b0 = (1 - cos(w0)) / 2 / a0; val b1 = (1 - cos(w0)) / a0; val b2 = b0
            val a1 = -2 * cos(w0) / a0; val a2 = (1 - alpha) / a0
            var x1 = 0.0; var x2 = 0.0; var y1 = 0.0; var y2 = 0.0
            val attack = 0.01
            val k = ln(0.0008 / note.vol) / (note.dur - attack)
            val kOver = ln(0.001 / 0.14) / (note.dur * 0.55)
            var i = 0
            while (i < len && start + i < n) {
                val t = i.toDouble() / RATE
                var env = if (t < attack) 0.0001 + (note.vol - 0.0001) * (t / attack) else note.vol * exp(k * (t - attack))
                if (t > note.dur) env = 0.0008 * max(0.0, 1 - (t - note.dur) / 0.02)
                val over = 0.14 * exp(kOver * t)
                val x = sin(2 * PI * note.f * t) + over * sin(2 * PI * note.f * 2 * t)
                val y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
                x2 = x1; x1 = x; y2 = y1; y1 = y
                mix[start + i] += y * env
                i++
            }
        }
        return ShortArray(n) { (tanh(mix[it] * 0.9 * 1.4) / 1.4 * 32767).toInt().coerceIn(-32768, 32767).toShort() }
    }

    private fun writeWav(f: File, pcm: ShortArray) {
        val data = ByteBuffer.allocate(pcm.size * 2).order(ByteOrder.LITTLE_ENDIAN).apply { pcm.forEach { putShort(it) } }.array()
        val h = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).apply {
            put("RIFF".toByteArray()); putInt(36 + data.size); put("WAVE".toByteArray()); put("fmt ".toByteArray())
            putInt(16); putShort(1); putShort(1); putInt(RATE); putInt(RATE * 2); putShort(2); putShort(16)
            put("data".toByteArray()); putInt(data.size)
        }.array()
        DataOutputStream(FileOutputStream(f)).use { it.write(h); it.write(data) }
    }
}

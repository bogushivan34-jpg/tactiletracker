package com.tactile.issues.data

// Gemini title extraction + image preparation (resize/compress/upload), mirroring the web app.

import android.content.Context
import android.graphics.Bitmap
import android.graphics.ImageDecoder
import android.net.Uri
import android.os.Build
import android.util.Base64
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.util.concurrent.TimeUnit

class Gemini(private val key: String, private val models: List<String> = listOf("gemini-3.5-flash-lite", "gemini-3.1-flash-lite-preview")) {
    private val client = OkHttpClient.Builder().callTimeout(20, TimeUnit.SECONDS).build()
    @Volatile private var working: String? = null

    val enabled get() = key.isNotBlank()

    fun extractTitle(prompt: String): String? {
        if (!enabled || prompt.isBlank()) return null
        val order = listOfNotNull(working) + models.filter { it != working }
        for (m in order) {
            try {
                call(m, prompt.trim())?.takeIf { it.isNotBlank() }?.let { working = m; return it.trim() }
            } catch (_: Exception) { }
        }
        return null
    }

    private fun call(model: String, prompt: String): String? {
        val body = JSONObject()
            .put("systemInstruction", JSONObject().put("parts", JSONArray().put(JSONObject().put("text", INSTRUCTION))))
            .put("contents", JSONArray().put(JSONObject().put("role", "user").put("parts", JSONArray().put(JSONObject().put("text", prompt)))))
            .put("generationConfig", JSONObject()
                .put("responseMimeType", "application/json")
                .put("responseSchema", JSONObject().put("type", "OBJECT")
                    .put("properties", JSONObject().put("title", JSONObject().put("type", "STRING").put("description", "Concise 3-8 word session title.")))
                    .put("required", JSONArray().put("title"))))
        val req = Request.Builder()
            .url("https://generativelanguage.googleapis.com/v1beta/models/$model:generateContent")
            .header("x-goog-api-key", key)
            .post(body.toString().toRequestBody("application/json".toMediaType()))
            .build()
        client.newCall(req).execute().use { r ->
            if (!r.isSuccessful) return null
            val root = JSONObject(r.body!!.string())
            val text = root.getJSONArray("candidates").getJSONObject(0).getJSONObject("content")
                .getJSONArray("parts").getJSONObject(0).getString("text")
            return JSONObject(text.trim()).optString("title").ifBlank { null }
        }
    }

    companion object {
        const val INSTRUCTION = "You extract a short, clear, actionable task title (3 to 8 words max) from a developer prompt intended for a coding session. " +
            "Preserve the language of the prompt. Strip conversational filler like \"So I want to\", \"By the way\", or \"Can you make\". " +
            "Return only the concise title in the JSON field `title`, with no trailing period and no quotes."
    }
}

object ImageTools {
    /** Coil model for a stored image reference: decoded bytes for data: URLs, the URL string otherwise. */
    private val dataCache = object : LinkedHashMap<String, ByteArray>(16, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, ByteArray>?) = size > 24
    }

    fun model(src: String): Any {
        if (!src.startsWith("data:")) return src
        synchronized(dataCache) {
            return dataCache.getOrPut(src) { Base64.decode(src.substringAfter(','), Base64.DEFAULT) }
        }
    }

    fun decode(ctx: Context, uri: Uri): Bitmap? = try {
        if (Build.VERSION.SDK_INT >= 28) {
            ImageDecoder.decodeBitmap(ImageDecoder.createSource(ctx.contentResolver, uri)) { dec, info, _ ->
                val w = info.size.width; val h = info.size.height
                val s = minOf(1.0, 1360.0 / maxOf(w, h))
                if (s < 1) dec.setTargetSize((w * s).toInt().coerceAtLeast(1), (h * s).toInt().coerceAtLeast(1))
                dec.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
            }
        } else {
            ctx.contentResolver.openInputStream(uri)?.use { android.graphics.BitmapFactory.decodeStream(it) }
        }
    } catch (e: Exception) { null }

    /** Resize to max 1360px, PNG for small/transparent else JPEG q84, try the bucket, else a data URL. Blocking. */
    fun prepare(bmp0: Bitmap, api: SupabaseApi?): String {
        val s = minOf(1.0, 1360.0 / maxOf(bmp0.width, bmp0.height))
        val bmp = if (s < 1) Bitmap.createScaledBitmap(bmp0, (bmp0.width * s).toInt(), (bmp0.height * s).toInt(), true) else bmp0
        val png = ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }.toByteArray()
        val (bytes, mime) = if (bmp.hasAlpha() || png.size <= 60 * 1024) png to "image/png"
        else ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.JPEG, 84, it) }.toByteArray() to "image/jpeg"
        return api?.tryUpload(bytes, mime) ?: ("data:$mime;base64," + Base64.encodeToString(bytes, Base64.NO_WRAP))
    }
}

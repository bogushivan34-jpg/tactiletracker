package com.tactile.issues.data

// Supabase REST client + background sync engine (same protocol as windows/src/Store.cs).

import com.tactile.issues.model.Issue
import com.tactile.issues.model.Project
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.TimeUnit

class TransientException(msg: String, cause: Throwable? = null) : Exception(msg, cause)
class RejectedException(msg: String) : Exception(msg)

class SupabaseApi(private val url: String, private val key: String, private val bucket: String = "attachments") {
    val client: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .build()
    private val json = "application/json".toMediaType()

    private fun req(path: String) = Request.Builder().url(url.trimEnd('/') + path)
        .header("apikey", key).header("Authorization", "Bearer $key").header("Accept", "application/json")

    private fun exec(r: Request): String {
        try {
            client.newCall(r).execute().use { resp ->
                val body = resp.body?.string().orEmpty()
                if (resp.isSuccessful) return body
                val c = resp.code
                if (c >= 500 || c == 429 || c == 408) throw TransientException("HTTP $c: $body")
                throw RejectedException("HTTP $c: $body")
            }
        } catch (e: IOException) {
            throw TransientException("Network: ${e.message}", e)
        }
    }

    fun select(table: String, query: String): JSONArray = JSONArray(exec(req("/rest/v1/$table?$query").get().build()))

    fun upsert(table: String, row: JSONObject) {
        exec(req("/rest/v1/$table?on_conflict=id").header("Prefer", "resolution=merge-duplicates,return=minimal")
            .post(row.toString().toRequestBody(json)).build())
    }

    fun delete(table: String, id: String) {
        exec(req("/rest/v1/$table?id=eq." + java.net.URLEncoder.encode(id, "UTF-8")).header("Prefer", "return=minimal").delete().build())
    }

    /** Public URL on success, null if the bucket is unavailable (caller falls back to a data URL). */
    fun tryUpload(bytes: ByteArray, mime: String): String? = try {
        val ext = when { "png" in mime -> "png"; "webp" in mime -> "webp"; else -> "jpg" }
        val path = "pasted/${System.currentTimeMillis()}_${java.util.UUID.randomUUID().toString().take(6)}.$ext"
        exec(req("/storage/v1/object/$bucket/$path").header("x-upsert", "true").post(bytes.toRequestBody(mime.toMediaType())).build())
        url.trimEnd('/') + "/storage/v1/object/public/$bucket/$path"
    } catch (e: Exception) { null }
}

enum class SyncState { Idle, Syncing, Offline, Disabled }

class SyncEngine(private val store: Store, private val api: SupabaseApi?, private val scope: CoroutineScope) {
    private val _status = MutableStateFlow(if (api == null) SyncState.Disabled else SyncState.Idle)
    val status: StateFlow<SyncState> get() = _status
    private val signal = Channel<Unit>(Channel.CONFLATED)
    private val flushLock = Mutex()
    @Volatile private var pullRequested = true
    @Volatile var lastPullMs = 0L
    var log: (String) -> Unit = {}

    fun start() {
        if (api == null) return
        scope.launch(Dispatchers.IO) {
            var failures = 0
            val backoff = longArrayOf(2_000, 4_000, 8_000, 15_000, 30_000, 60_000)
            while (isActive) {
                val ok = try {
                    flush() && (if (pullRequested) { pullRequested = false; pull() } else true)
                } catch (e: Exception) { log("sync error: $e"); false }
                if (ok) {
                    failures = 0
                    _status.value = SyncState.Idle
                    signal.receive()
                } else {
                    failures++
                    _status.value = SyncState.Offline
                    withTimeoutOrNull(backoff[minOf(failures - 1, backoff.size - 1)]) { signal.receive() }
                    pullRequested = true
                }
            }
        }
    }

    fun kick() { signal.trySend(Unit) }
    fun requestPull() { pullRequested = true; signal.trySend(Unit) }

    /** Sends the outbox. Returns false if the network is unavailable (retry later). */
    suspend fun flush(): Boolean = flushLock.withLock {
        val api = api ?: return true
        while (true) {
            val head = store.state.value.outbox.firstOrNull() ?: return true
            _status.value = SyncState.Syncing
            val colon = head.key.indexOf(':')
            val kind = head.key.substring(0, colon)
            val id = head.key.substring(colon + 1)
            val d = store.state.value
            try {
                when (kind) {
                    "issue" -> d.issues.firstOrNull { it.id == id }?.let { api.upsert("issues", Rows.issueJson(it)) }
                    "project" -> d.projects.firstOrNull { it.id == id }?.let { api.upsert("projects", Rows.projectJson(it)) }
                    "issue-del" -> api.delete("issues", id)
                    "project-del" -> api.delete("projects", id)
                }
            } catch (e: TransientException) {
                log("deferred ${head.key}: ${e.message}")
                return false
            } catch (e: Exception) {
                // Permanent rejection: drop it so the queue never wedges; the local copy stays intact.
                log("rejected ${head.key}: ${e.message}")
            }
            store.ack(head) // only removes it if it wasn't re-queued meanwhile
        }
        @Suppress("UNREACHABLE_CODE") true
    }

    private fun pull(): Boolean {
        val api = api ?: return true
        val version = store.changeCounter.get()
        _status.value = SyncState.Syncing
        val projects: List<Project>? = try {
            api.select("projects", "select=*&order=sort_order.asc").let { a -> (0 until a.length()).mapNotNull { Rows.project(a.getJSONObject(it), it) } }
        } catch (e: RejectedException) { log("projects unavailable: ${e.message}"); null }
        val issuesArr = api.select("issues", "select=*&order=created_at.desc")
        val issues: List<Issue> = (0 until issuesArr.length()).mapNotNull { Rows.issue(issuesArr.getJSONObject(it)) }
        if (store.applyPull(version, projects, issues)) lastPullMs = System.currentTimeMillis()
        else pullRequested = true // user edited meanwhile; try again after the outbox drains
        if (pullRequested) signal.trySend(Unit)
        return true
    }
}

package com.tactile.issues.data

// Supabase REST client + background sync engine (same protocol as windows/src/Store.cs).

import com.tactile.issues.model.Issue
import com.tactile.issues.model.Project
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
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

class TransientException(msg: String, cause: Throwable? = null, val code: Int = 0) : Exception(msg, cause)
class RejectedException(msg: String, val code: Int = 0) : Exception(msg)

/** Why the last sync attempt failed, in words a user can act on. */
data class SyncProblem(val kind: Kind, val title: String, val detail: String, val sinceMs: Long) {
    enum class Kind { NoInternet, Unreachable, Timeout, Server, Auth, Rejected, Other }

    companion object {
        fun from(e: Throwable?, networkUp: Boolean, now: Long = System.currentTimeMillis()): SyncProblem {
            val chain = generateSequence(e) { it.cause }.toList()
            fun has(pred: (Throwable) -> Boolean) = chain.any(pred)
            val code = (e as? TransientException)?.code ?: (e as? RejectedException)?.code ?: 0
            val (kind, title, detail) = when {
                !networkUp -> Triple(Kind.NoInternet, "No internet connection",
                    "Your phone is offline. Changes are saved on this phone and will sync when you're back online.")
                has { it is java.net.UnknownHostException } -> Triple(Kind.Unreachable, "Can't reach the server",
                    "The sync server's address couldn't be found. It may be blocked on this network. Try turning on your VPN.")
                has { it is java.net.SocketTimeoutException || it is java.io.InterruptedIOException } -> Triple(Kind.Timeout, "Server isn't responding",
                    "The connection timed out. The sync server may be blocked on this network. Try turning on your VPN.")
                has { it is java.net.ConnectException || it is java.net.NoRouteToHostException } -> Triple(Kind.Unreachable, "Can't connect to the server",
                    "The connection was refused. Check your connection, or try turning on your VPN.")
                has { it is javax.net.ssl.SSLException } -> Triple(Kind.Unreachable, "Secure connection failed",
                    "The secure connection to the sync server was interrupted. This network may be filtering traffic. Try turning on your VPN.")
                code == 401 || code == 403 -> Triple(Kind.Auth, "Access denied",
                    "The server rejected the app's key (HTTP $code). The Supabase key in this build may be wrong.")
                e is TransientException && code > 0 -> Triple(Kind.Server, "Server error",
                    "The sync server returned HTTP $code. It will retry automatically.")
                e is RejectedException -> Triple(Kind.Rejected, "Server rejected the request",
                    "HTTP $code. Your data is still saved on this phone.")
                else -> Triple(Kind.Other, "Sync failed", e?.message?.take(160) ?: "Unknown error.")
            }
            return SyncProblem(kind, title, detail, now)
        }
    }
}

class SupabaseApi(private val url: String, private val key: String, private val bucket: String = "attachments") {
    val client: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(25, TimeUnit.SECONDS)
        .callTimeout(40, TimeUnit.SECONDS)
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
                if (c >= 500 || c == 429 || c == 408) throw TransientException("HTTP $c: $body", code = c)
                throw RejectedException("HTTP $c: $body", c)
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

class SyncEngine(
    private val store: Store,
    private val api: SupabaseApi?,
    private val scope: CoroutineScope,
    initialLastSyncMs: Long = 0L,
) {
    private val _status = MutableStateFlow(if (api == null) SyncState.Disabled else SyncState.Idle)
    val status: StateFlow<SyncState> get() = _status

    private val _problem = MutableStateFlow<SyncProblem?>(null)
    /** Set while syncing keeps failing; cleared by the next successful round. */
    val problem: StateFlow<SyncProblem?> get() = _problem

    private val _lastSyncMs = MutableStateFlow(initialLastSyncMs)
    /** Wall-clock time of the last fully successful sync (0 = never). */
    val lastSyncMs: StateFlow<Long> get() = _lastSyncMs

    private val _events = MutableSharedFlow<String>(extraBufferCapacity = 8)
    /** One-off messages for a snackbar ("Back online", "A change was rejected"). */
    val events: SharedFlow<String> get() = _events

    var networkAvailable: () -> Boolean = { true }
    var onSynced: (Long) -> Unit = {}
    @Volatile private var lastFailure: Throwable? = null
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
                lastFailure = null
                val ok = try {
                    flush() && (if (pullRequested) { pullRequested = false; pull() } else true)
                } catch (e: Exception) { log("sync error: $e"); lastFailure = e; false }
                if (ok) {
                    failures = 0
                    val now = System.currentTimeMillis()
                    _lastSyncMs.value = now
                    onSynced(now)
                    if (_problem.value != null) { _problem.value = null; _events.tryEmit("Back online · synced") }
                    _status.value = SyncState.Idle
                    signal.receive()
                } else {
                    failures++
                    val p = SyncProblem.from(lastFailure, networkAvailable())
                    val prev = _problem.value
                    // Keep the original "since" time while the same kind of failure repeats.
                    _problem.value = if (prev != null && prev.kind == p.kind) p.copy(sinceMs = prev.sinceMs) else p
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
                lastFailure = e
                return false
            } catch (e: Exception) {
                // Permanent rejection: drop it so the queue never wedges; the local copy stays intact.
                log("rejected ${head.key}: ${e.message}")
                val code = (e as? RejectedException)?.code
                _events.tryEmit("The server rejected a change" + (if (code != null) " (HTTP $code)" else "") + ". It's kept on this phone only.")
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

package com.tactile.issues.data

// Local-first state + persistence + outbox. Pure JVM (no Android APIs) so it is unit-testable.
//  * Startup reads one small JSON file; the UI renders from it immediately.
//  * Every edit updates state atomically, queues an outbox key, and saves (debounced, atomic rename).

import com.tactile.issues.model.Issue
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Project
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.concurrent.atomic.AtomicLong

data class OutboxItem(val key: String, val seq: Long) // "issue:<id>", "issue-del:<id>", "project:<id>", "project-del:<id>"

data class AppData(
    val projects: List<Project> = Logic.initialProjects(),
    val issues: List<Issue> = emptyList(),
    val activeProjectId: String = Logic.DEFAULT_PROJECT_ID,
    val outbox: List<OutboxItem> = emptyList(),
    val hasEverSynced: Boolean = false,
) {
    val activeProject: Project get() = projects.firstOrNull { it.id == activeProjectId } ?: projects.first()
}

class Store(private val dir: File, private val scope: CoroutineScope) {
    private val file = File(dir, "data.json")
    private val _state = MutableStateFlow(AppData())
    val state: StateFlow<AppData> get() = _state
    val changeCounter = AtomicLong(0)
    private val seq = AtomicLong(0)
    private var saveJob: Job? = null
    var onChanged: (() -> Unit)? = null

    fun load() {
        if (!file.exists()) return
        try {
            val root = JSONObject(file.readText())
            val projects = root.optJSONArray("projects")?.let { a ->
                (0 until a.length()).mapNotNull { Rows.project(a.getJSONObject(it), it) }
            }.orEmpty()
            val issues = root.optJSONArray("issues")?.let { a ->
                (0 until a.length()).mapNotNull { Rows.issue(a.getJSONObject(it)) }
            }.orEmpty()
            val outbox = root.optJSONArray("outbox")?.let { a ->
                (0 until a.length()).map { a.getJSONObject(it) }.map { OutboxItem(it.getString("key"), it.optLong("seq")) }
            }.orEmpty()
            seq.set(outbox.maxOfOrNull { it.seq } ?: 0)
            val ps = if (projects.isNotEmpty()) Logic.sortProjects(projects) else Logic.initialProjects()
            val active = root.optString("activeProjectId", Logic.DEFAULT_PROJECT_ID)
            _state.value = AppData(
                projects = ps,
                issues = issues,
                activeProjectId = if (ps.any { it.id == active }) active else ps.first().id,
                outbox = outbox,
                hasEverSynced = root.optBoolean("hasEverSynced"),
            )
        } catch (e: Exception) {
            file.copyTo(File(dir, "data.corrupt-${System.currentTimeMillis()}.json"), overwrite = true)
        }
    }

    private fun serialize(d: AppData): String = JSONObject().apply {
        put("version", 1)
        put("activeProjectId", d.activeProjectId)
        put("hasEverSynced", d.hasEverSynced)
        put("projects", JSONArray().apply { d.projects.forEach { put(Rows.projectJson(it)) } })
        put("issues", JSONArray().apply { d.issues.forEach { put(Rows.issueJson(it)) } })
        put("outbox", JSONArray().apply { d.outbox.forEach { put(JSONObject().put("key", it.key).put("seq", it.seq)) } })
    }.toString()

    fun saveSoon() {
        saveJob?.cancel()
        saveJob = scope.launch(Dispatchers.IO) {
            delay(250)
            saveNow()
        }
    }

    @Synchronized
    fun saveNow() {
        dir.mkdirs()
        val tmp = File(dir, "data.json.tmp")
        tmp.writeText(serialize(_state.value))
        if (!tmp.renameTo(file)) { file.delete(); tmp.renameTo(file) }
    }

    /** View-only change (no sync), e.g. selected project. */
    fun setLocal(f: (AppData) -> AppData) {
        _state.update(f)
        saveSoon()
    }

    /** A user edit: apply, queue the given outbox keys, persist, notify sync. */
    fun edit(keys: List<String>, f: (AppData) -> AppData) {
        _state.update { d ->
            var out = d.outbox
            for (k in keys) out = enqueue(out, k)
            f(d).copy(outbox = out)
        }
        changeCounter.incrementAndGet()
        saveSoon()
        onChanged?.invoke()
    }

    /** Local edit that should not be synced yet (e.g. live drag reorder), but must block stale pulls. */
    fun touch(f: (AppData) -> AppData) {
        _state.update(f)
        changeCounter.incrementAndGet()
        saveSoon()
    }

    private fun enqueue(out: List<OutboxItem>, key: String): List<OutboxItem> {
        val colon = key.indexOf(':')
        val base = key.substring(0, colon).removeSuffix("-del")
        val id = key.substring(colon + 1)
        return out.filter { it.key != "$base:$id" && it.key != "$base-del:$id" } + OutboxItem(key, seq.incrementAndGet())
    }

    fun ack(item: OutboxItem) {
        _state.update { d -> d.copy(outbox = d.outbox.filterNot { it.key == item.key && it.seq == item.seq }) }
        saveSoon()
    }

    /** Applies a pull only if nothing changed locally since [version]; returns false otherwise. */
    fun applyPull(version: Long, remoteProjects: List<Project>?, remoteIssues: List<Issue>): Boolean {
        var applied = false
        _state.update { d ->
            if (changeCounter.get() != version || d.outbox.isNotEmpty()) { applied = false; return@update d }
            applied = true
            val order = d.issues.map { it.id }
            val expanded = d.issues.associate { it.id to it.expanded }
            val merged = remoteIssues.map { r -> expanded[r.id]?.let { r.copy(expanded = it) } ?: r }
            val projects = if (!remoteProjects.isNullOrEmpty()) Logic.sortProjects(remoteProjects) else d.projects
            d.copy(
                issues = Logic.sortInitial(merged, order),
                projects = projects,
                activeProjectId = if (projects.any { it.id == d.activeProjectId }) d.activeProjectId else projects.first().id,
                hasEverSynced = true,
            )
        }
        if (applied) saveSoon()
        return applied
    }
}

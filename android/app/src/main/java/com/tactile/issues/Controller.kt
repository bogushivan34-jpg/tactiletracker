package com.tactile.issues

// All user actions (port of App.tsx handlers / windows MainWindow mutations).

import android.content.Context
import android.net.Uri
import com.tactile.issues.data.ImageTools
import com.tactile.issues.model.Issue
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Status
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

data class SharedPayload(val text: String, val images: List<Uri>)

class Controller(private val app: TactileApp) {
    private val store get() = app.store
    val extracting = MutableStateFlow<Set<String>>(emptySet())
    val pendingShare = MutableStateFlow<SharedPayload?>(null)
    val focusComposer = MutableStateFlow(0)
    var lastDeleted: Pair<Issue, Int>? = null
        private set

    // ---------- projects ----------
    fun selectProject(id: String) {
        if (id == store.state.value.activeProjectId) return
        Sound.expand(true)
        store.setLocal { it.copy(activeProjectId = id) }
    }

    fun createProject(name: String) {
        val p = Logic.createProject(name, store.state.value.projects) ?: return
        Sound.play("add")
        store.edit(listOf("project:${p.id}")) { it.copy(projects = it.projects + p, activeProjectId = p.id) }
    }

    fun renameProject(id: String, name: String) {
        if (name.isBlank()) return
        store.edit(listOf("project:$id")) { d -> d.copy(projects = d.projects.map { if (it.id == id) it.copy(name = name.trim()) else it }) }
    }

    fun cycleProjectColor(id: String) {
        Sound.play("in_work")
        store.edit(listOf("project:$id")) { d -> d.copy(projects = d.projects.map { if (it.id == id) it.copy(colorKey = Logic.nextCycledColor(it.colorKey)) else it }) }
    }

    fun deleteProject(id: String) {
        val d0 = store.state.value
        if (d0.projects.size <= 1) return
        Sound.expand(false)
        val removed = d0.issues.filter { it.projectId == id }.map { "issue-del:${it.id}" }
        store.edit(removed + "project-del:$id") { d ->
            val ps = d.projects.filter { it.id != id }
            d.copy(projects = ps, issues = d.issues.filter { it.projectId != id },
                activeProjectId = if (d.activeProjectId == id) ps.first().id else d.activeProjectId)
        }
    }

    // ---------- issues ----------
    fun addIssue(raw: String, status: Status, images: List<String>) {
        val d = store.state.value
        val issue = Logic.createIssue(raw, d.activeProject.id, status, images) ?: return
        Sound.play("add")
        store.edit(listOf("issue:${issue.id}")) { it.copy(issues = listOf(issue) + it.issues) }
        if (Logic.isPromptMode(raw) && app.gemini.enabled) {
            extracting.update { it + issue.id }
            app.scope.launch {
                val title = withContext(Dispatchers.IO) { runCatching { app.gemini.extractTitle(raw) }.getOrNull() }
                extracting.update { it - issue.id }
                if (!title.isNullOrBlank() && store.state.value.issues.any { it.id == issue.id }) {
                    store.edit(listOf("issue:${issue.id}")) { s -> s.copy(issues = Logic.update(s.issues, issue.id) { it.copy(title = title) }) }
                }
            }
        }
    }

    private fun editIssue(id: String, f: (Issue) -> Issue) =
        store.edit(listOf("issue:$id")) { it.copy(issues = Logic.update(it.issues, id, f)) }

    fun cycleStatus(id: String) {
        val i = store.state.value.issues.firstOrNull { it.id == id } ?: return
        val next = Logic.nextStatus(i.status)
        Sound.status(next)
        store.edit(listOf("issue:$id")) { it.copy(issues = Logic.setStatus(it.issues, id, next)) }
    }

    fun setStatus(id: String, s: Status) {
        Sound.status(s)
        store.edit(listOf("issue:$id")) { it.copy(issues = Logic.setStatus(it.issues, id, s)) }
    }

    fun toggleExpand(id: String) {
        val i = store.state.value.issues.firstOrNull { it.id == id } ?: return
        Sound.expand(!i.expanded)
        editIssue(id) { it.copy(expanded = !it.expanded) }
    }

    fun updateTitle(id: String, title: String) {
        if (title.isBlank()) { deleteIssue(id); return }
        editIssue(id) { it.copy(title = title.trim()) }
    }

    fun deleteIssue(id: String) {
        val d = store.state.value
        val idx = d.issues.indexOfFirst { it.id == id }
        if (idx < 0) return
        lastDeleted = d.issues[idx] to idx
        Sound.expand(false)
        store.edit(listOf("issue-del:$id")) { it.copy(issues = it.issues.filter { i -> i.id != id }) }
    }

    fun undoDelete() {
        val (issue, idx) = lastDeleted ?: return
        lastDeleted = null
        Sound.play("add")
        store.edit(listOf("issue:${issue.id}")) { d ->
            if (d.issues.any { it.id == issue.id }) d
            else d.copy(issues = d.issues.toMutableList().apply { add(idx.coerceIn(0, size), issue) })
        }
    }

    fun addComment(id: String, text: String, images: List<String>) {
        if (text.isBlank() && images.isEmpty()) return
        Sound.play("add")
        editIssue(id) { Logic.addComment(it, text, images) }
    }

    fun updateComment(id: String, cid: String, text: String) = editIssue(id) { Logic.updateComment(it, cid, text) }

    fun deleteComment(id: String, cid: String) {
        Sound.expand(false)
        editIssue(id) { Logic.deleteComment(it, cid) }
    }

    fun deleteCommentImage(id: String, cid: String, index: Int) {
        Sound.expand(false)
        editIssue(id) { Logic.deleteCommentImage(it, cid, index) }
    }

    // ---------- drag reorder ----------
    fun dragStart() = Sound.play("drag_start")

    fun dragOver(draggedId: String, targetId: String) {
        store.touch { it.copy(issues = Logic.reorder(it.issues, draggedId, targetId)) }
    }

    fun dragEnd(id: String) {
        Sound.play("drop")
        store.edit(listOf("issue:$id")) { it }
    }

    // ---------- images ----------
    fun prepareImages(ctx: Context, uris: List<Uri>, onEach: (String?) -> Unit) {
        for (u in uris) {
            app.scope.launch {
                val url = withContext(Dispatchers.IO) {
                    val api = if (BuildConfig.SUPABASE_KEY.isNotBlank()) app.api else null
                    runCatching { ImageTools.decode(ctx, u)?.let { ImageTools.prepare(it, api) } }.getOrNull()
                }
                onEach(url)
            }
        }
    }
}

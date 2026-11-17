package com.tactile.issues.model

// Pure model + logic, ported from src/lib/issuesLogic.ts and projectsLogic.ts
// (same behavior as windows/src/Model.cs). No Android dependencies: unit-testable on the JVM.

import java.time.Instant
import java.time.format.DateTimeFormatter
import kotlin.random.Random

enum class Status { IN_WORK, TASK, BACKLOG, COMPLETED }

data class Comment(
    val id: String,
    val text: String,
    val createdAt: String,
    val images: List<String> = emptyList(),
)

data class Issue(
    val id: String,
    val projectId: String,
    val section: String, // "prompted" | "drafts"
    val title: String,
    val completed: Boolean,
    val active: Boolean,
    val expanded: Boolean,
    val comments: List<Comment>,
    val createdAt: String,
) {
    val status: Status
        get() = when {
            completed -> Status.COMPLETED
            active -> Status.IN_WORK
            section == "drafts" -> Status.BACKLOG
            else -> Status.TASK
        }
    val attachmentCount: Int get() = comments.sumOf { it.images.size }
}

data class Project(
    val id: String,
    val name: String,
    val colorKey: String,
    val sortOrder: Double,
    val createdAt: String,
)

object Logic {
    const val DEFAULT_PROJECT_ID = "proj-default"
    const val PROMPT_MODE_MIN_CHARS = 100
    val COLOR_ORDER = listOf("coral", "blue", "emerald", "violet", "amber", "rose", "teal")

    fun nowIso(): String = DateTimeFormatter.ISO_INSTANT.format(Instant.now().let { Instant.ofEpochMilli(it.toEpochMilli()) })

    fun newId(prefix: String, randLen: Int): String {
        val digits = "0123456789abcdefghijklmnopqrstuvwxyz"
        val rand = (1..randLen).map { digits[Random.nextInt(36)] }.joinToString("")
        return prefix + "_" + System.currentTimeMillis().toString(36) + "_" + rand
    }

    fun initialProjects() = listOf(
        Project(DEFAULT_PROJECT_ID, "Main", "coral", 0.0, "2026-11-01T00:00:00.000Z"),
        Project("proj-blue", "Studio", "blue", 1.0, "2026-11-01T00:01:00.000Z"),
        Project("proj-emerald", "Lab", "emerald", 2.0, "2026-11-01T00:02:00.000Z"),
    )

    fun sortProjects(p: List<Project>) = p.sortedWith(compareBy<Project> { it.sortOrder }.thenBy { it.createdAt })

    fun isPromptMode(text: String) = text.trim().length >= PROMPT_MODE_MIN_CHARS

    fun fallbackTitle(prompt: String): String {
        val cleaned = prompt.replace(Regex("\\s+"), " ").trim()
        if (cleaned.isEmpty()) return ""
        if (cleaned.length <= 52) return cleaned.trimEnd('.', '!', '?')
        val slice = cleaned.split(" ").take(7).joinToString(" ").trimEnd(',', '.', ';', ':', '!', '?')
        return "$slice…"
    }

    fun nextStatus(s: Status) = when (s) {
        Status.TASK -> Status.IN_WORK
        Status.IN_WORK -> Status.BACKLOG
        Status.BACKLOG -> Status.COMPLETED
        Status.COMPLETED -> Status.TASK
    }

    fun applyStatus(i: Issue, s: Status): Issue = when (s) {
        Status.IN_WORK -> i.copy(completed = false, active = true, section = "prompted")
        Status.BACKLOG -> i.copy(completed = false, active = false, section = "drafts")
        Status.COMPLETED -> i.copy(completed = true, active = false)
        Status.TASK -> i.copy(completed = false, active = false, section = "prompted")
    }

    fun rank(i: Issue) = when (i.status) {
        Status.IN_WORK -> 0; Status.TASK -> 1; Status.BACKLOG -> 2; Status.COMPLETED -> 3
    }

    fun setStatus(issues: List<Issue>, id: String, s: Status): List<Issue> {
        val target = issues.firstOrNull { it.id == id } ?: return issues
        val updated = applyStatus(target, s)
        return if (s == Status.IN_WORK) listOf(updated) + issues.filter { it.id != id }
        else issues.map { if (it.id == id) updated else it }
    }

    fun cycleStatus(issues: List<Issue>, id: String): List<Issue> {
        val t = issues.firstOrNull { it.id == id } ?: return issues
        return setStatus(issues, id, nextStatus(t.status))
    }

    /** Move dragged onto target's position; dragged adopts target's status. */
    fun reorder(issues: List<Issue>, draggedId: String, targetId: String): List<Issue> {
        if (draggedId == targetId) return issues
        val di = issues.indexOfFirst { it.id == draggedId }
        val ti = issues.indexOfFirst { it.id == targetId }
        if (di < 0 || ti < 0) return issues
        val dragged = applyStatus(issues[di], issues[ti].status)
        val rest = issues.toMutableList().apply { removeAt(di) }
        val nti = rest.indexOfFirst { it.id == targetId }
        rest.add(if (di < ti) nti + 1 else nti, dragged)
        return rest
    }

    fun createIssue(title: String, projectId: String, status: Status, images: List<String> = emptyList()): Issue? {
        val raw = title.trim()
        val clean = images.filter { it.isNotBlank() }
        if (raw.isEmpty() && clean.isEmpty()) return null
        val now = nowIso()
        val base = Issue(
            id = newId("iss", 5),
            projectId = projectId.ifBlank { DEFAULT_PROJECT_ID },
            section = if (status == Status.BACKLOG) "drafts" else "prompted",
            title = "",
            completed = status == Status.COMPLETED,
            active = status == Status.IN_WORK,
            expanded = false,
            comments = emptyList(),
            createdAt = now,
        )
        if (isPromptMode(raw)) {
            return base.copy(title = fallbackTitle(raw), comments = listOf(Comment(newId("cmt", 5), raw, now, clean)))
        }
        return base.copy(
            title = raw.ifEmpty { "Pasted image attachment" },
            comments = if (clean.isNotEmpty()) listOf(Comment(newId("cmt", 5), "", now, clean)) else emptyList(),
        )
    }

    fun update(issues: List<Issue>, id: String, f: (Issue) -> Issue) = issues.map { if (it.id == id) f(it) else it }

    fun addComment(i: Issue, text: String, images: List<String>): Issue {
        val t = text.trim()
        if (t.isEmpty() && images.isEmpty()) return i
        return i.copy(expanded = true, comments = i.comments + Comment(newId("cmt", 5), t, nowIso(), images))
    }

    fun updateComment(i: Issue, commentId: String, newText: String): Issue {
        val t = newText.trim()
        return i.copy(comments = i.comments.mapNotNull { c ->
            if (c.id != commentId) c else if (t.isEmpty() && c.images.isEmpty()) null else c.copy(text = t)
        })
    }

    fun deleteComment(i: Issue, commentId: String) = i.copy(comments = i.comments.filter { it.id != commentId })

    fun deleteCommentImage(i: Issue, commentId: String, index: Int) = i.copy(comments = i.comments.mapNotNull { c ->
        if (c.id != commentId) c
        else {
            val imgs = c.images.filterIndexed { k, _ -> k != index }
            if (c.text.isBlank() && imgs.isEmpty()) null else c.copy(images = imgs)
        }
    })

    /** Rank, then saved order; items missing from the saved order (created elsewhere) go first, newest first. */
    fun sortInitial(issues: List<Issue>, savedOrder: List<String>): List<Issue> {
        val pos = savedOrder.withIndex().associate { it.value to it.index }
        return issues.sortedWith(
            compareBy<Issue> { rank(it) }
                .thenBy { pos[it.id] ?: -1 }
                .thenByDescending { it.createdAt }
        )
    }

    fun visibleMain(projectIssues: List<Issue>) = projectIssues.filter { !it.completed }.sortedBy { rank(it) }

    fun nextProjectColor(projects: List<Project>): String {
        val used = projects.map { it.colorKey }.toSet()
        return COLOR_ORDER.firstOrNull { it !in used } ?: COLOR_ORDER[projects.size % COLOR_ORDER.size]
    }

    fun nextCycledColor(c: String): String {
        val i = COLOR_ORDER.indexOf(c)
        return COLOR_ORDER[if (i < 0) 0 else (i + 1) % COLOR_ORDER.size]
    }

    fun createProject(name: String, existing: List<Project>): Project? {
        val t = name.trim()
        if (t.isEmpty()) return null
        return Project(newId("proj", 4), t, nextProjectColor(existing), (existing.maxOfOrNull { it.sortOrder } ?: -1.0) + 1, nowIso())
    }
}

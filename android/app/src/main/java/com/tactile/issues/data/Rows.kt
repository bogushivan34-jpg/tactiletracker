package com.tactile.issues.data

// Wire format <-> model. Identical row shapes to the Supabase tables used by web + Windows.

import com.tactile.issues.model.Comment
import com.tactile.issues.model.Issue
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Project
import org.json.JSONArray
import org.json.JSONObject

object Rows {
    private fun JSONObject.str(k: String): String? = if (has(k) && !isNull(k)) opt(k)?.toString() else null
    private fun JSONObject.bool(k: String): Boolean = when (val v = opt(k)) {
        is Boolean -> v
        is Number -> v.toInt() != 0
        is String -> v == "true" || v == "1"
        else -> false
    }

    fun normalizeComments(raw: Any?): List<Comment> {
        val arr = raw as? JSONArray ?: (raw as? String)?.let { runCatching { JSONArray(it) }.getOrNull() } ?: return emptyList()
        val out = ArrayList<Comment>()
        for (idx in 0 until arr.length()) {
            when (val e = arr.opt(idx)) {
                is String -> {
                    val t = e.trim()
                    if (t.isNotEmpty()) out += Comment("comment-legacy-$idx-${t.take(8)}", t, "1970-01-01T00:00:00.000Z")
                }
                is JSONObject -> {
                    if (e.str("id") == "__entry_theme_meta__" || e.str("text") == "__theme_metadata__" || e.has("theme") || e.has("themeCandidates")) continue
                    val text = (e.str("text") ?: e.str("body") ?: e.str("content") ?: "").trim()
                    val imgs = (e.opt("images") as? JSONArray)?.let { a ->
                        (0 until a.length()).mapNotNull { (a.opt(it) as? String)?.trim()?.takeIf { s -> s.isNotEmpty() } }
                    } ?: emptyList()
                    if (text.isEmpty() && imgs.isEmpty()) continue
                    val id = e.str("id")?.trim()?.takeIf { it.isNotEmpty() } ?: "comment-$idx-${System.nanoTime()}"
                    out += Comment(id, text, e.str("createdAt") ?: e.str("created_at") ?: Logic.nowIso(), imgs)
                }
            }
        }
        return out
    }

    fun issue(o: JSONObject): Issue? {
        val id = o.str("id")?.trim().orEmpty()
        if (id.isEmpty()) return null
        val pid = (o.str("project_id") ?: o.str("projectId"))?.trim()?.takeIf { it.isNotEmpty() } ?: Logic.DEFAULT_PROJECT_ID
        val section = when (o.str("section")) { "drafts", "draft", "backlog" -> "drafts"; else -> "prompted" }
        return Issue(
            id = id, projectId = pid, section = section, title = o.str("title") ?: "",
            completed = o.bool("completed"), active = o.bool("active"), expanded = o.bool("expanded"),
            comments = normalizeComments(o.opt("comments")),
            createdAt = o.str("created_at")?.takeIf { it.isNotEmpty() } ?: Logic.nowIso(),
        )
    }

    fun issueJson(i: Issue) = JSONObject().apply {
        put("id", i.id); put("section", i.section); put("title", i.title)
        put("completed", i.completed); put("active", i.active); put("expanded", i.expanded)
        put("comments", JSONArray().apply {
            i.comments.forEach { c ->
                put(JSONObject().apply {
                    put("id", c.id); put("text", c.text); put("createdAt", c.createdAt)
                    if (c.images.isNotEmpty()) put("images", JSONArray(c.images))
                })
            }
        })
        put("created_at", i.createdAt); put("project_id", i.projectId)
    }

    fun project(o: JSONObject, fallbackIndex: Int): Project? {
        val id = o.str("id")?.trim().orEmpty()
        if (id.isEmpty()) return null
        val order = (o.opt("sort_order") ?: o.opt("sortOrder")) as? Number
        val color = o.str("color_key") ?: o.str("colorKey")
        return Project(
            id = id,
            name = o.str("name")?.trim()?.takeIf { it.isNotEmpty() } ?: "Untitled Project",
            colorKey = if (color in Logic.COLOR_ORDER) color!! else "coral",
            sortOrder = order?.toDouble() ?: fallbackIndex.toDouble(),
            createdAt = o.str("created_at") ?: o.str("createdAt") ?: Logic.nowIso(),
        )
    }

    fun projectJson(p: Project) = JSONObject().apply {
        put("id", p.id); put("name", p.name); put("color_key", p.colorKey)
        // Keep integral orders integral (sort_order is an integer column).
        if (p.sortOrder % 1.0 == 0.0) put("sort_order", p.sortOrder.toLong()) else put("sort_order", p.sortOrder)
        put("created_at", p.createdAt)
    }
}

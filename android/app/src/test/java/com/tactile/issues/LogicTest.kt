package com.tactile.issues

import com.tactile.issues.data.Rows
import com.tactile.issues.model.Comment
import com.tactile.issues.model.Issue
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Status
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LogicTest {
    private fun issue(id: String, status: Status = Status.TASK) =
        Logic.applyStatus(Issue(id, "p", "prompted", id, false, false, false, emptyList(), "2026-11-0${id.length}T00:00:00Z"), status)

    @Test fun statusCycle() {
        assertEquals(Status.IN_WORK, Logic.nextStatus(Status.TASK))
        assertEquals(Status.BACKLOG, Logic.nextStatus(Status.IN_WORK))
        assertEquals(Status.COMPLETED, Logic.nextStatus(Status.BACKLOG))
        assertEquals(Status.TASK, Logic.nextStatus(Status.COMPLETED))
    }

    @Test fun runningPinsToTop() {
        val list = listOf(issue("a"), issue("b"), issue("c"))
        val r = Logic.setStatus(list, "c", Status.IN_WORK)
        assertEquals(listOf("c", "a", "b"), r.map { it.id })
        assertEquals(Status.IN_WORK, r[0].status)
    }

    @Test fun reorderAdoptsTargetStatus() {
        val list = listOf(issue("a", Status.IN_WORK), issue("b"), issue("c", Status.BACKLOG))
        val r = Logic.reorder(list, "c", "a")
        assertEquals(listOf("c", "a", "b"), r.map { it.id })
        assertEquals(Status.IN_WORK, r[0].status)
        val down = Logic.reorder(list, "a", "b")
        assertEquals(listOf("b", "a", "c"), down.map { it.id })
    }

    @Test fun promptModeKeepsPromptAsFirstComment() {
        val long = "So I want to build a really long prompt that goes past one hundred characters so that prompt mode kicks in for sure."
        val i = Logic.createIssue(long, "p", Status.TASK)!!
        assertEquals(1, i.comments.size)
        assertEquals(long, i.comments[0].text)
        assertTrue(i.title.endsWith("…"))
        assertNull(Logic.createIssue("   ", "p", Status.TASK))
        assertEquals("Pasted image attachment", Logic.createIssue("", "p", Status.TASK, listOf("data:x"))!!.title)
        assertEquals("drafts", Logic.createIssue("idea", "p", Status.BACKLOG)!!.section)
    }

    @Test fun commentEditsAndImageRemoval() {
        var i = issue("a")
        i = Logic.addComment(i, "hello", emptyList())
        assertTrue(i.expanded)
        i = Logic.updateComment(i, i.comments[0].id, "edited")
        assertEquals("edited", i.comments[0].text)
        i = Logic.addComment(i, "", listOf("img1"))
        i = Logic.deleteCommentImage(i, i.comments[1].id, 0)
        assertEquals(1, i.comments.size)
        i = Logic.updateComment(i, i.comments[0].id, "  ")
        assertEquals(0, i.comments.size)
    }

    @Test fun sortInitialPutsUnknownFirstNewestFirst() {
        val a = issue("a"); val bb = issue("bb"); val ccc = issue("ccc")
        val r = Logic.sortInitial(listOf(a, bb, ccc), listOf("a", "bb"))
        assertEquals(listOf("ccc", "a", "bb"), r.map { it.id })
    }

    @Test fun rowRoundTripMatchesWebShape() {
        val i = issue("a").copy(comments = listOf(Comment("c1", "t", "2026-01-01T00:00:00Z", listOf("u1"))))
        val j = Rows.issueJson(i)
        assertEquals("p", j.getString("project_id"))
        assertEquals(i, Rows.issue(JSONObject(j.toString())))
        // legacy + junk comments are normalized like issuesLogic.ts
        val legacy = JSONObject("""{"id":"x","section":"backlog","comments":["old note",{"id":"__entry_theme_meta__"},{"body":"b"}]}""")
        val n = Rows.issue(legacy)!!
        assertEquals("drafts", n.section)
        assertEquals(listOf("old note", "b"), n.comments.map { it.text })
        assertEquals(Logic.DEFAULT_PROJECT_ID, n.projectId)
    }

    @Test fun projectColors() {
        val ps = Logic.initialProjects()
        assertEquals("violet", Logic.nextProjectColor(ps))
        assertEquals("coral", Logic.nextCycledColor("teal"))
        assertEquals(3.0, Logic.createProject("X", ps)!!.sortOrder, 0.0)
    }
}

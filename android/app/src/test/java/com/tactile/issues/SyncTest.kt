package com.tactile.issues

// End-to-end sync test against windows/tools/mock_supabase.py (never the real database).
// Run the mock first:  python windows/tools/mock_supabase.py 8767
// Skipped automatically when the mock isn't running.

import com.tactile.issues.data.Rows
import com.tactile.issues.data.Store
import com.tactile.issues.data.SupabaseApi
import com.tactile.issues.data.SyncEngine
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Status
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.File
import java.net.Socket

class SyncTest {
    private val url = "http://127.0.0.1:8767"

    private fun mockUp() = runCatching { Socket("127.0.0.1", 8767).close(); true }.getOrDefault(false)

    private suspend fun until(ms: Long = 10_000, cond: () -> Boolean) = withTimeout(ms) { while (!cond()) delay(30) }

    @Test fun fullRoundTrip() = runBlocking {
        assumeTrue("mock server not running", mockUp())
        val dir = File(System.getProperty("java.io.tmpdir"), "tactile-sync-" + System.nanoTime())
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        val api = SupabaseApi(url, "test-key")
        val store = Store(dir, scope)
        val sync = SyncEngine(store, api, scope)
        store.onChanged = { sync.kick() }
        sync.log = { println("sync: $it") }
        sync.start()

        until { sync.lastPullMs > 0 }
        val before = api.select("issues", "select=*").length()
        assertEquals(before, store.state.value.issues.size)

        // Create project + issues, mutate, delete
        val p = Logic.createProject("Sync test", store.state.value.projects)!!
        store.edit(listOf("project:${p.id}")) { it.copy(projects = it.projects + p, activeProjectId = p.id) }
        val a = Logic.createIssue("first", p.id, Status.TASK)!!
        val b = Logic.createIssue("second", p.id, Status.BACKLOG)!!
        store.edit(listOf("issue:${a.id}", "issue:${b.id}")) { it.copy(issues = listOf(a, b) + it.issues) }
        store.edit(listOf("issue:${a.id}")) { it.copy(issues = Logic.setStatus(it.issues, a.id, Status.IN_WORK)) }
        store.edit(listOf("issue:${a.id}")) { d -> d.copy(issues = Logic.update(d.issues, a.id) { Logic.addComment(it, "note", emptyList()) }) }
        store.edit(listOf("issue-del:${b.id}")) { d -> d.copy(issues = d.issues.filter { it.id != b.id }) }
        until { store.state.value.outbox.isEmpty() }

        val remote = api.select("issues", "select=*")
        val ra = (0 until remote.length()).map { remote.getJSONObject(it) }.first { it.getString("id") == a.id }
        assertTrue(ra.getBoolean("active"))
        assertEquals("note", Rows.issue(ra)!!.comments.single().text)
        assertFalse((0 until remote.length()).any { remote.getJSONObject(it).getString("id") == b.id })
        val rp = api.select("projects", "select=*")
        assertTrue((0 until rp.length()).any { rp.getJSONObject(it).getString("id") == p.id })

        // A pull must not clobber an edit made while it's in flight
        val v = store.changeCounter.get()
        store.edit(listOf("issue:${a.id}")) { d -> d.copy(issues = Logic.update(d.issues, a.id) { it.copy(title = "local wins") }) }
        assertFalse(store.applyPull(v, null, emptyList()))
        assertEquals("local wins", store.state.value.issues.first { it.id == a.id }.title)
        until { store.state.value.outbox.isEmpty() }

        // Cascade delete + persistence
        store.edit(listOf("issue-del:${a.id}", "project-del:${p.id}")) { d ->
            d.copy(issues = d.issues.filter { it.projectId != p.id }, projects = d.projects.filter { it.id != p.id }, activeProjectId = d.projects.first().id)
        }
        until { store.state.value.outbox.isEmpty() }
        assertEquals(before, api.select("issues", "select=*").length())
        store.saveNow()
        val reloaded = Store(dir, scope).apply { load() }
        assertEquals(store.state.value.issues.map { it.id }, reloaded.state.value.issues.map { it.id })
        scope.cancel()
        dir.deleteRecursively()
        Unit
    }

    @Test fun offlineEditsQueueAndSurvive() = runBlocking {
        val dir = File(System.getProperty("java.io.tmpdir"), "tactile-offline-" + System.nanoTime())
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        val store = Store(dir, scope)
        val sync = SyncEngine(store, SupabaseApi("http://127.0.0.1:9", "k"), scope) // nothing listens on port 9
        val a = Logic.createIssue("offline", Logic.DEFAULT_PROJECT_ID, Status.TASK)!!
        store.edit(listOf("issue:${a.id}")) { it.copy(issues = listOf(a)) }
        assertFalse(sync.flush())
        assertEquals(1, store.state.value.outbox.size)
        store.saveNow()
        val reloaded = Store(dir, scope).apply { load() }
        assertEquals(listOf("issue:${a.id}"), reloaded.state.value.outbox.map { it.key })
        scope.cancel()
        dir.deleteRecursively()
        Unit
    }
}

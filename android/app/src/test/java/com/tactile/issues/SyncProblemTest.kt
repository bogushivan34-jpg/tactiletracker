package com.tactile.issues

import com.tactile.issues.data.RejectedException
import com.tactile.issues.data.Store
import com.tactile.issues.data.SupabaseApi
import com.tactile.issues.data.SyncEngine
import com.tactile.issues.data.SyncProblem
import com.tactile.issues.data.SyncProblem.Kind
import com.tactile.issues.data.TransientException
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Status
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.net.ConnectException
import java.net.SocketTimeoutException
import java.net.UnknownHostException

class SyncProblemTest {
    private fun kind(e: Throwable?, net: Boolean = true) = SyncProblem.from(e, net).kind

    @Test fun classifiesFailures() {
        // A VPN-blocked host usually shows up as DNS failure or a timeout, wrapped by SupabaseApi.
        assertEquals(Kind.Unreachable, kind(TransientException("Network", UnknownHostException("x.supabase.co"))))
        assertEquals(Kind.Timeout, kind(TransientException("Network", SocketTimeoutException("timeout"))))
        assertEquals(Kind.Unreachable, kind(TransientException("Network", ConnectException("refused"))))
        assertEquals(Kind.NoInternet, kind(TransientException("Network", UnknownHostException("x")), net = false))
        assertEquals(Kind.Auth, kind(RejectedException("HTTP 401", 401)))
        assertEquals(Kind.Server, kind(TransientException("HTTP 503", code = 503)))
        assertEquals(Kind.Rejected, kind(RejectedException("HTTP 400", 400)))
        assertEquals(Kind.Other, kind(null))
        assertTrue(SyncProblem.from(TransientException("n", UnknownHostException("x")), true).detail.contains("VPN"))
    }

    @Test fun engineReportsUnreachableServer() = runBlocking {
        val dir = File(System.getProperty("java.io.tmpdir"), "tactile-problem-" + System.nanoTime())
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        val store = Store(dir, scope)
        val sync = SyncEngine(store, SupabaseApi("http://127.0.0.1:9", "k"), scope) // nothing listens on port 9
        val a = Logic.createIssue("queued", Logic.DEFAULT_PROJECT_ID, Status.TASK)!!
        store.edit(listOf("issue:${a.id}")) { it.copy(issues = listOf(a)) }
        sync.start()
        val p = withTimeout(20_000) { sync.problem.first { it != null } }!!
        assertEquals(Kind.Unreachable, p.kind)
        assertEquals(0L, sync.lastSyncMs.value)
        scope.cancel()
        dir.deleteRecursively()
        Unit
    }
}

package com.tactile.issues

import android.app.Application
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.util.Log
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.tactile.issues.data.Gemini
import com.tactile.issues.data.Store
import com.tactile.issues.data.SupabaseApi
import com.tactile.issues.data.SyncEngine
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.Dispatchers
import java.io.File
import java.util.concurrent.TimeUnit

class TactileApp : Application() {
    lateinit var store: Store
    lateinit var sync: SyncEngine
    lateinit var api: SupabaseApi
    lateinit var gemini: Gemini
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    override fun onCreate() {
        super.onCreate()
        store = Store(File(filesDir, "tactile"), scope)
        store.load()
        api = SupabaseApi(BuildConfig.SUPABASE_URL, BuildConfig.SUPABASE_KEY, BuildConfig.SUPABASE_BUCKET)
        val prefs = getSharedPreferences("sync", MODE_PRIVATE)
        sync = SyncEngine(store, if (BuildConfig.SUPABASE_KEY.isNotBlank()) api else null, scope, prefs.getLong("lastSyncMs", 0L))
        sync.log = { Log.i("TactileSync", it) }
        sync.onSynced = { prefs.edit().putLong("lastSyncMs", it).apply() }
        val cm = getSystemService(ConnectivityManager::class.java)
        sync.networkAvailable = {
            runCatching {
                val caps = cm.getNetworkCapabilities(cm.activeNetwork)
                caps != null && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            }.getOrDefault(true)
        }
        // Retry right away when a network (or a VPN) comes up instead of waiting out the backoff.
        runCatching {
            cm.registerDefaultNetworkCallback(object : ConnectivityManager.NetworkCallback() {
                override fun onAvailable(network: Network) { if (sync.problem.value != null) sync.requestPull() }
                override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                    if (sync.problem.value != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) sync.requestPull()
                }
            })
        }
        gemini = Gemini(BuildConfig.GEMINI_KEY)
        store.onChanged = { sync.kick() }
        Sound.init(this)

        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStart(owner: LifecycleOwner) {
                if (System.currentTimeMillis() - sync.lastPullMs > 20_000) sync.requestPull()
            }
            override fun onStop(owner: LifecycleOwner) {
                store.saveNow()
                // Make sure queued edits reach the server even if the process is killed.
                if (store.state.value.outbox.isNotEmpty()) scheduleFlush(this@TactileApp)
            }
        })
    }

    val controller by lazy { Controller(this) }
    private var syncStarted = false

    fun startSyncOnce() {
        if (syncStarted) return
        syncStarted = true
        sync.start()
    }

    companion object {
        fun get(ctx: Context) = ctx.applicationContext as TactileApp

        fun scheduleFlush(ctx: Context) {
            val req = OneTimeWorkRequestBuilder<FlushWorker>()
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS)
                .build()
            WorkManager.getInstance(ctx).enqueueUniqueWork("flush", ExistingWorkPolicy.REPLACE, req)
        }
    }
}

class FlushWorker(ctx: Context, params: WorkerParameters) : CoroutineWorker(ctx, params) {
    override suspend fun doWork(): Result {
        val app = get(applicationContext)
        val ok = app.sync.flush()
        app.store.saveNow()
        return if (ok) Result.success() else Result.retry()
    }

    private fun get(ctx: Context) = TactileApp.get(ctx)
}

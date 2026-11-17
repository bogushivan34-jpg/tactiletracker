package com.tactile.issues

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.ui.graphics.toArgb
import com.tactile.issues.ui.MainScreen

class MainActivity : ComponentActivity() {
    private val app get() = TactileApp.get(this)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.light(android.graphics.Color.TRANSPARENT, android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.light(android.graphics.Color.WHITE, android.graphics.Color.WHITE),
        )
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) handleIntent(intent)
        setContent { MainScreen(app, app.controller) }
        // Network sync starts only after the first frame is on screen.
        window.decorView.post {
            reportFullyDrawn()
            app.startSyncOnce()
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    private fun handleIntent(intent: Intent?) {
        intent ?: return
        val ctl = app.controller
        when (intent.action) {
            Intent.ACTION_SEND -> {
                val text = listOfNotNull(intent.getStringExtra(Intent.EXTRA_SUBJECT), intent.getStringExtra(Intent.EXTRA_TEXT))
                    .filter { it.isNotBlank() }.distinct().joinToString("\n")
                val img: Uri? = if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
                    else @Suppress("DEPRECATION") intent.getParcelableExtra(Intent.EXTRA_STREAM)
                ctl.pendingShare.value = SharedPayload(text, listOfNotNull(img?.takeIf { intent.type?.startsWith("image/") == true }))
            }
            Intent.ACTION_SEND_MULTIPLE -> {
                val imgs: List<Uri> = (if (Build.VERSION.SDK_INT >= 33) intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java)
                    else @Suppress("DEPRECATION") intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM)).orEmpty()
                ctl.pendingShare.value = SharedPayload(intent.getStringExtra(Intent.EXTRA_TEXT).orEmpty(), imgs)
            }
            ACTION_NEW_TASK -> ctl.focusComposer.value = ctl.focusComposer.value + 1
        }
    }

    companion object {
        const val ACTION_NEW_TASK = "com.tactile.issues.NEW_TASK"
    }
}

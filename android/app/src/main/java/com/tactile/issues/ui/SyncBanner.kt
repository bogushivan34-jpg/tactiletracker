package com.tactile.issues.ui

// The "not synced" banner: says why sync failed (no internet / server blocked / key rejected),
// when the last good sync was, how many edits are waiting, and offers Retry.

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.tactile.issues.data.SyncProblem
import kotlinx.coroutines.delay

private val AmberBg = Color(0xFFFFFBEB)
private val AmberBorder = Color(0xFFFDE68A)
private val AmberIcon = Color(0xFFB45309)
private val RoseBg = Color(0xFFFFF1F2)
private val RoseBorder = Color(0xFFFECDD3)
private val RoseIcon = Color(0xFFBE123C)

/** Current time, refreshed every 30 s so "5 min ago" stays honest. */
@Composable
fun rememberNow(): Long {
    val now by produceState(System.currentTimeMillis()) { while (true) { delay(30_000); value = System.currentTimeMillis() } }
    return now
}

fun ago(ms: Long, now: Long): String {
    if (ms <= 0L) return "never"
    val s = ((now - ms) / 1000).coerceAtLeast(0)
    return when {
        s < 60 -> "just now"
        s < 3600 -> "${s / 60} min ago"
        s < 86_400 -> "${s / 3600} h ago"
        else -> "${s / 86_400} d ago"
    }
}

@Composable
fun SyncBanner(
    problem: SyncProblem?,
    visible: Boolean,
    retrying: Boolean,
    lastSyncMs: Long,
    pending: Int,
    onRetry: () -> Unit,
    onDismiss: () -> Unit,
) {
    // Keep the last problem on screen while the exit animation runs.
    val last = remember { arrayOfNulls<SyncProblem>(1) }
    if (problem != null) last[0] = problem
    val now = rememberNow()
    AnimatedVisibility(
        visible = visible && problem != null,
        enter = expandVertically() + fadeIn(),
        exit = shrinkVertically() + fadeOut(),
    ) {
        val shown = last[0] ?: return@AnimatedVisibility
        val serious = shown.kind == SyncProblem.Kind.Auth || shown.kind == SyncProblem.Kind.Rejected
        val bg = if (serious) RoseBg else AmberBg
        val border = if (serious) RoseBorder else AmberBorder
        val icon = if (serious) RoseIcon else AmberIcon
        val shape = RoundedCornerShape(14.dp)
        Row(
            Modifier
                .padding(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 2.dp)
                .fillMaxWidth()
                .background(bg, shape)
                .border(1.dp, border, shape)
                .padding(start = 12.dp, top = 10.dp, bottom = 10.dp, end = 4.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Ico(Icons.cloudOff, 18.dp, icon, Modifier.padding(top = 1.dp))
            Spacer(Modifier.width(10.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(shown.title, style = text(13.5.sp, FontWeight.SemiBold, Slate.s900))
                Text(shown.detail, style = text(12.5.sp, color = Slate.s600, lineHeight = 17.sp))
                val parts = buildList {
                    add(if (lastSyncMs > 0) "Last synced ${ago(lastSyncMs, now)}" else "Not synced yet on this phone")
                    if (pending > 0) add("$pending change${if (pending == 1) "" else "s"} waiting")
                }
                Text(parts.joinToString(" · "), style = mono(10.5.sp, Slate.s500))
                Spacer(Modifier.height(2.dp))
                Pill({ if (!retrying) onRetry() }, bg = Color.White, border = border, lip = true, height = 28.dp, hPad = 12.dp) {
                    if (retrying) {
                        RunningDots(icon)
                        PillText("Retrying", Slate.s700, 12.sp)
                    } else {
                        Ico(Icons.rotate, 13.dp, Slate.s700)
                        PillText("Retry now", Slate.s700, 12.sp)
                    }
                }
            }
            Box(
                Modifier.size(36.dp).clickable(remember { MutableInteractionSource() }, null, onClick = onDismiss),
                contentAlignment = Alignment.Center,
            ) { Ico(Icons.x, 14.dp, Slate.s400) }
        }
    }
}

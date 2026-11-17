package com.tactile.issues.ui

// One issue: morphing status pill, inline-editable title, Done, comments pill, expandable drawer.
// Port of components/IssueRow.tsx (hover affordances become tap / long-press on touch).

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil3.compose.AsyncImage
import com.tactile.issues.Controller
import com.tactile.issues.data.ImageTools
import com.tactile.issues.model.Comment
import com.tactile.issues.model.Issue
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Status

@Composable
fun IssueRow(
    issue: Issue,
    ctl: Controller,
    extracting: Boolean,
    isLast: Boolean,
    onPreview: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val th = LocalTheme.current
    val status = issue.status
    val running = status == Status.IN_WORK
    val backlog = status == Status.BACKLOG
    val view = LocalView.current

    val container = if (running) {
        Modifier
            .padding(vertical = 8.dp)
            .shadow(14.dp, RoundedCornerShape(16.dp), ambientColor = th.accent, spotColor = th.accent.copy(alpha = 0.55f))
            .background(th.tintBg, RoundedCornerShape(16.dp))
            .border(1.dp, th.tintBorder, RoundedCornerShape(16.dp))
            .padding(horizontal = 12.dp, vertical = 2.dp)
    } else {
        Modifier
            .graphicsLayer { alpha = if (backlog) 0.56f else 1f }
            .drawBehind {
                if (!isLast) drawLine(Slate.s100, Offset(0f, size.height - 0.5f), Offset(size.width, size.height - 0.5f), 1.dp.toPx())
            }
    }

    Column(modifier.then(container)) {
        Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            StatusPill(status) { view.tick(); ctl.cycleStatus(issue.id) }
            Spacer(Modifier.width(10.dp))
            Box(Modifier.weight(1f)) { Title(issue, status, extracting, ctl) }
            Spacer(Modifier.width(6.dp))
            if (running) {
                Pill(
                    onClick = { view.confirm(); ctl.setStatus(issue.id, Status.COMPLETED) },
                    bg = Color.White.copy(alpha = 0.9f), border = th.tintBorder, height = 28.dp,
                ) {
                    Ico(Icons.check, 12.dp, Slate.s700)
                    PillText("Done", Slate.s700)
                }
            }
            CommentsPill(issue, running) { view.tick(); ctl.toggleExpand(issue.id) }
        }
        AnimatedVisibility(
            visible = issue.expanded,
            enter = expandVertically(Springs.drawerSize) + fadeIn(tween(220)),
            exit = shrinkVertically(tween(220)) + fadeOut(tween(160)),
        ) {
            Drawer(issue, running, ctl, onPreview)
        }
    }
}

@Composable
fun StatusPill(status: Status, onClick: () -> Unit) {
    val th = LocalTheme.current
    when (status) {
        Status.IN_WORK -> Pill(onClick, bg = th.accent, border = th.accentStrong, lip = true) {
            RunningDots(Color.White); PillText("Running", Color.White)
        }
        Status.TASK -> Pill(onClick, bg = Slate.s100, border = Slate.s200.copy(alpha = 0.9f), lip = true) {
            Ico(Icons.play, 10.dp, Slate.s700); PillText("Task", Slate.s700)
        }
        Status.BACKLOG -> Pill(onClick, bg = Slate.s100.copy(alpha = 0.8f), dashed = Slate.s300, lip = true) {
            Ico(Icons.lightbulb, 12.dp, Slate.s500); PillText("Idea", Slate.s500)
        }
        Status.COMPLETED -> Pill(onClick, bg = Slate.e50, border = Slate.e200, lip = true) {
            Ico(Icons.rotate, 12.dp, Slate.e700); PillText("Restore", Slate.e700)
        }
    }
}

@Composable
private fun Title(issue: Issue, status: Status, extracting: Boolean, ctl: Controller) {
    val th = LocalTheme.current
    var editing by remember(issue.id) { mutableStateOf(false) }
    if (editing) {
        var value by remember { mutableStateOf(TextFieldValue(issue.title, TextRange(0, issue.title.length))) }
        val focus = remember { FocusRequester() }
        var hadFocus by remember { mutableStateOf(false) }
        fun commit() {
            if (!editing) return
            editing = false
            val v = value.text.trim()
            if (v != issue.title) ctl.updateTitle(issue.id, v)
        }
        BasicTextField(
            value, { value = it },
            textStyle = text(15.sp, FontWeight.Medium, Slate.s900),
            cursorBrush = SolidColor(th.accent),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { commit() }),
            modifier = Modifier
                .fillMaxWidth()
                .focusRequester(focus)
                .onFocusChanged { if (it.isFocused) hadFocus = true else if (hadFocus) commit() }
                .drawBehind { drawLine(Slate.s300, Offset(0f, size.height), Offset(size.width, size.height), 1.dp.toPx()) }
                .padding(bottom = 2.dp),
        )
        LaunchedEffect(Unit) { focus.requestFocus() }
        return
    }
    val style = when {
        status == Status.COMPLETED -> text(14.5.sp, FontWeight.Medium, Slate.s400).copy(textDecoration = TextDecoration.LineThrough)
        extracting -> text(15.sp, FontWeight.Medium, Slate.s600)
        status == Status.IN_WORK -> text(15.5.sp, FontWeight.SemiBold, Slate.s950)
        status == Status.BACKLOG -> text(14.5.sp, FontWeight.Normal, Slate.s600)
        else -> text(15.sp, FontWeight.Medium, Slate.s900)
    }
    val pulse = if (extracting) {
        rememberInfiniteTransition(label = "pulse").animateFloat(0.35f, 0.75f, infiniteRepeatable(tween(900), RepeatMode.Reverse), label = "p").value
    } else 1f
    Text(
        issue.title, style = style.copy(lineHeight = 20.sp),
        modifier = Modifier
            .graphicsLayer { alpha = pulse }
            .combinedClickableCompat(onClick = { editing = true }),
    )
}

@OptIn(ExperimentalFoundationApi::class)
private fun Modifier.combinedClickableCompat(onClick: () -> Unit, onLongClick: (() -> Unit)? = null) =
    this.then(Modifier.combinedClickable(interactionSource = null, indication = null, onClick = onClick, onLongClick = onLongClick))

@Composable
private fun CommentsPill(issue: Issue, running: Boolean, onClick: () -> Unit) {
    val th = LocalTheme.current
    val count = issue.comments.size
    val att = issue.attachmentCount
    val (bg, fg, border) = when {
        issue.expanded -> Triple(if (running) th.accent else Slate.s900, Color.White, null)
        count > 0 -> if (running) Triple(Color.White.copy(alpha = 0.85f), Slate.s800, th.tintDrawerBorder) else Triple(Color.Transparent, Slate.s700, null)
        else -> Triple(Color.Transparent, if (running) th.tintMuted else Slate.s400, null)
    }
    Pill(onClick, bg = bg, border = border, height = 28.dp, pressScale = 0.88f) {
        if (att > 0) Ico(Icons.image, 13.5.dp, fg)
        Ico(Icons.message, 14.dp, fg)
        if (count > 0) Text(count.toString(), style = mono(11.5.sp, fg))
    }
}

// ------------------------------------------------------------------ drawer

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun Drawer(issue: Issue, running: Boolean, ctl: Controller, onPreview: (String) -> Unit) {
    val th = LocalTheme.current
    val ctx = LocalContext.current
    val view = LocalView.current
    val staged = remember(issue.id) { mutableStateListOf<String>() }
    var uploading by remember(issue.id) { mutableIntStateOf(0) }
    var draft by rememberSaveable(issue.id) { mutableStateOf("") }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(8)) { uris: List<Uri> ->
        if (uris.isNotEmpty()) {
            com.tactile.issues.Sound.play("add")
            uploading += uris.size
            ctl.prepareImages(ctx, uris) { url -> uploading--; if (url != null) staged.add(url) }
        }
    }

    Column(
        Modifier
            .padding(start = 4.dp, bottom = 12.dp)
            .fillMaxWidth()
            .background(if (running) Color.White.copy(alpha = 0.9f) else Slate.s50.copy(alpha = 0.85f), RoundedCornerShape(12.dp))
            .border(1.dp, if (running) th.tintDrawerBorder else Slate.s200.copy(alpha = 0.75f), RoundedCornerShape(12.dp))
            .padding(horizontal = 14.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        issue.comments.forEach { c -> CommentItem(issue, c, ctl, onPreview) }

        if (staged.isNotEmpty() || uploading > 0) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                staged.forEachIndexed { k, src -> StagedThumb(src, 56) { staged.removeAt(k) } }
                repeat(uploading) { UploadingThumb(56) }
            }
        }

        Row(verticalAlignment = Alignment.CenterVertically) {
            Pill({ picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) }, height = 24.dp, hPad = 4.dp) {
                Ico(Icons.image, 15.dp, Slate.s400)
            }
            Spacer(Modifier.width(6.dp))
            val canSend = (draft.isNotBlank() || staged.isNotEmpty()) && uploading == 0
            fun send() {
                if (!canSend) return
                view.tick()
                ctl.addComment(issue.id, draft, staged.toList())
                draft = ""; staged.clear()
            }
            Box(Modifier.weight(1f)) {
                if (draft.isEmpty()) Text("Write a comment or add an image…", style = text(13.5.sp, color = Slate.s400), maxLines = 1)
                BasicTextField(
                    draft, { draft = it },
                    textStyle = text(13.5.sp, color = Slate.s900),
                    cursorBrush = SolidColor(th.accent),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                    keyboardActions = KeyboardActions(onSend = { send() }),
                    modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
                )
            }
            Pill({ send() }, bg = if (canSend) th.accent else Color.Transparent, lip = canSend, height = 24.dp, enabled = canSend) {
                Ico(Icons.send, 12.dp, if (canSend) Color.White else Slate.s900)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun CommentItem(issue: Issue, c: Comment, ctl: Controller, onPreview: (String) -> Unit) {
    val th = LocalTheme.current
    val ctx = LocalContext.current
    var editing by remember(c.id) { mutableStateOf(false) }
    var menu by remember { mutableStateOf(false) }
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        if (editing) {
            var value by remember { mutableStateOf(TextFieldValue(c.text, TextRange(c.text.length))) }
            val focus = remember { FocusRequester() }
            var hadFocus by remember { mutableStateOf(false) }
            fun commit() {
                if (!editing) return
                editing = false
                val v = value.text.trim()
                if (v.isEmpty() && c.images.isEmpty()) ctl.deleteComment(issue.id, c.id)
                else if (v != c.text) ctl.updateComment(issue.id, c.id, v)
            }
            Row(verticalAlignment = Alignment.Top) {
                BasicTextField(
                    value, { value = it },
                    textStyle = text(13.5.sp, color = Slate.s900, lineHeight = 22.sp),
                    cursorBrush = SolidColor(th.accent),
                    modifier = Modifier
                        .weight(1f)
                        .focusRequester(focus)
                        .onFocusChanged { if (it.isFocused) hadFocus = true else if (hadFocus) commit() }
                        .background(Color.White, RoundedCornerShape(8.dp))
                        .border(1.dp, th.accent, RoundedCornerShape(8.dp))
                        .padding(horizontal = 10.dp, vertical = 4.dp),
                )
                Pill({ commit() }, bg = th.accent, height = 24.dp, hPad = 5.dp) { Ico(Icons.check, 14.dp, Color.White) }
            }
            LaunchedEffect(Unit) { focus.requestFocus() }
        } else {
            Box {
                val label = if (c.text.isNotEmpty()) c.text
                else (if (c.images.size > 1) "Attached images" else "Attached image") + " — tap to add note"
                Text(
                    label,
                    style = if (c.text.isNotEmpty()) text(13.5.sp, color = Slate.s600, lineHeight = 22.sp) else text(12.sp, color = Slate.s400),
                    modifier = Modifier.fillMaxWidth().combinedClickableCompat(onClick = { editing = true }, onLongClick = { menu = true }),
                )
                DropdownMenu(menu, { menu = false }) {
                    DropdownMenuItem({ Text("Copy text", style = text(14.sp)) }, onClick = {
                        menu = false
                        (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("comment", c.text))
                    })
                    DropdownMenuItem({ Text("Edit", style = text(14.sp)) }, onClick = { menu = false; editing = true })
                    DropdownMenuItem({ Text("Delete", style = text(14.sp, color = Color(0xFFBE123C))) }, onClick = { menu = false; ctl.deleteComment(issue.id, c.id) })
                }
            }
        }
        if (c.images.isNotEmpty()) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                c.images.forEachIndexed { k, src ->
                    Attachment(src, onOpen = { onPreview(src) }, onRemove = { ctl.deleteCommentImage(issue.id, c.id, k) })
                }
            }
        }
    }
}

@Composable
private fun Attachment(src: String, onOpen: () -> Unit, onRemove: () -> Unit) {
    Box {
        AsyncImage(
            model = ImageTools.model(src), contentDescription = "Attachment",
            contentScale = ContentScale.Fit,
            modifier = Modifier
                .sizeIn(minWidth = 48.dp, minHeight = 48.dp, maxWidth = 260.dp, maxHeight = 210.dp)
                .clip(RoundedCornerShape(12.dp))
                .border(1.dp, Slate.s200.copy(alpha = 0.9f), RoundedCornerShape(12.dp))
                .background(Slate.s100)
                .combinedClickableCompat(onClick = onOpen),
        )
        Box(
            Modifier.align(Alignment.TopEnd).padding(6.dp).size(22.dp)
                .background(Slate.s900.copy(alpha = 0.75f), CircleShape)
                .combinedClickableCompat(onClick = onRemove),
            contentAlignment = Alignment.Center,
        ) { Ico(Icons.x, 12.dp, Color.White) }
    }
}

@Composable
fun StagedThumb(src: String, size: Int, onRemove: () -> Unit) {
    Box(Modifier.size(size.dp)) {
        AsyncImage(
            model = ImageTools.model(src), contentDescription = null, contentScale = ContentScale.Crop,
            modifier = Modifier.size(size.dp).clip(RoundedCornerShape(10.dp)).border(1.dp, Slate.s200, RoundedCornerShape(10.dp)),
        )
        Box(
            Modifier.align(Alignment.TopEnd).padding(4.dp).size(18.dp)
                .background(Slate.s900.copy(alpha = 0.8f), CircleShape)
                .combinedClickableCompat(onClick = onRemove),
            contentAlignment = Alignment.Center,
        ) { Ico(Icons.x, 10.dp, Color.White) }
    }
}

@Composable
fun UploadingThumb(size: Int) {
    val angle by rememberInfiniteTransition(label = "spin").animateFloat(0f, 360f, infiniteRepeatable(tween(900, easing = androidx.compose.animation.core.LinearEasing)), label = "a")
    Box(
        Modifier.size(size.dp).dashedBorder(Slate.s300, 10.dp, 1.dp).background(Slate.s50, RoundedCornerShape(10.dp)),
        contentAlignment = Alignment.Center,
    ) { Ico(Icons.loader, 16.dp, Slate.s400, Modifier.graphicsLayer { rotationZ = angle }) }
}

package com.tactile.issues.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectDragGesturesAfterLongPress
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.material3.SwipeToDismissBox
import androidx.compose.material3.SwipeToDismissBoxValue
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.rememberSwipeToDismissBoxState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.compose.ui.zIndex
import coil3.compose.AsyncImage
import com.tactile.issues.Controller
import com.tactile.issues.Sound
import com.tactile.issues.TactileApp
import com.tactile.issues.data.AppData
import com.tactile.issues.data.ImageTools
import com.tactile.issues.data.SyncState
import com.tactile.issues.model.Issue
import com.tactile.issues.model.Logic
import com.tactile.issues.model.Project
import com.tactile.issues.model.Status
import kotlinx.coroutines.launch

@Composable
fun MainScreen(app: TactileApp, ctl: Controller) {
    val data by app.store.state.collectAsState()
    val extracting by ctl.extracting.collectAsState()
    val sync by app.sync.status.collectAsState()
    var preview by remember { mutableStateOf<String?>(null) }
    var showCompleted by rememberSaveable { mutableStateOf(false) }
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val project = data.activeProject

    val onDelete: (String) -> Unit = { id ->
        ctl.deleteIssue(id)
        scope.launch {
            snackbar.currentSnackbarData?.dismiss()
            val r = snackbar.showSnackbar("Deleted", actionLabel = "Undo", duration = SnackbarDuration.Short)
            if (r == SnackbarResult.ActionPerformed) ctl.undoDelete()
        }
    }

    AnimatedProjectTheme(project.colorKey) {
        val th = LocalTheme.current
        Box(
            Modifier
                .fillMaxSize()
                .background(Color.White)
                .drawBehind {
                    drawRect(Brush.verticalGradient(listOf(th.tintBgSubtle, Color.White), 0f, 170.dp.toPx() + 40.dp.toPx()), size = size.copy(height = 210.dp.toPx()))
                },
            contentAlignment = Alignment.TopCenter,
        ) {
            Column(Modifier.widthIn(max = 760.dp).fillMaxSize().statusBarsPadding()) {
                Carousel(data, ctl)
                Header(project, data, sync, onCompleted = { Sound.expand(true); showCompleted = true }, onRefresh = { app.sync.requestPull() })
                IssueList(data, ctl, extracting, onPreview = { preview = it }, onDelete = onDelete, refreshing = sync == SyncState.Syncing, onRefresh = { app.sync.requestPull() }, modifier = Modifier.weight(1f))
                Composer(ctl, Modifier.navigationBarsPadding().imePadding())
            }
            SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter).navigationBarsPadding().imePadding().padding(bottom = 72.dp)) { d ->
                Snackbar(
                    d, shape = RoundedCornerShape(14.dp), containerColor = Slate.s900, contentColor = Color.White,
                    actionColor = th.tintBorder,
                )
            }
        }

        if (showCompleted) CompletedSheet(data, ctl, extracting, onPreview = { preview = it }) { Sound.expand(false); showCompleted = false }
        preview?.let { Lightbox(it) { preview = null } }
    }
}

// ================================================================== carousel

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun Carousel(data: AppData, ctl: Controller) {
    val listState = rememberLazyListState()
    val view = LocalView.current
    var adding by remember { mutableStateOf(false) }
    var renaming by remember { mutableStateOf<Project?>(null) }
    var deleting by remember { mutableStateOf<Project?>(null) }
    val activeIdx = data.projects.indexOfFirst { it.id == data.activeProjectId }.coerceAtLeast(0)
    LaunchedEffect(activeIdx) { listState.animateScrollToItem(activeIdx, -120) }
    val counts = remember(data.issues) { data.issues.filter { !it.completed }.groupingBy { it.projectId }.eachCount() }

    Column(Modifier.padding(top = 10.dp, bottom = 4.dp)) {
        LazyRow(
            state = listState,
            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            items(data.projects, key = { it.id }) { p ->
                var menu by remember { mutableStateOf(false) }
                Box(Modifier.animateItem()) {
                    ProjectChip(
                        p, active = p.id == data.activeProjectId, count = counts[p.id] ?: 0,
                        onClick = { view.tick(); ctl.selectProject(p.id) },
                        onDot = { view.tick(); if (p.id == data.activeProjectId) ctl.cycleProjectColor(p.id) else ctl.selectProject(p.id) },
                        onLong = { view.heavy(); menu = true },
                    )
                    DropdownMenu(menu, { menu = false }) {
                        DropdownMenuItem({ Text("Rename", style = text(14.sp)) }, onClick = { menu = false; renaming = p })
                        DropdownMenuItem({ Text("Change color", style = text(14.sp)) }, onClick = { menu = false; ctl.cycleProjectColor(p.id) })
                        if (data.projects.size > 1) DropdownMenuItem({ Text("Delete project", style = text(14.sp, color = Color(0xFFBE123C))) }, onClick = { menu = false; deleting = p })
                    }
                }
            }
            item(key = "__add") {
                Pill({ adding = true }, dashed = Slate.s300, height = 32.dp, pressScale = 0.94f) {
                    Ico(Icons.plus, 14.dp, Slate.s500)
                    PillText("Project", Slate.s500, 12.sp, FontWeight.Medium)
                }
            }
        }
        if (data.projects.size > 1) {
            Row(Modifier.fillMaxWidth().padding(top = 2.dp), horizontalArrangement = Arrangement.Center) {
                data.projects.forEach { p ->
                    val active = p.id == data.activeProjectId
                    val w by animateDpAsState(if (active) 20.dp else 6.dp, label = "dot")
                    Box(
                        Modifier.padding(horizontal = 3.dp).height(6.dp).width(w)
                            .background(if (active) themeFor(p.colorKey).accent else Slate.s300, RoundedCornerShape(3.dp))
                    )
                }
            }
        }
    }

    if (adding) NameDialog("New project", "", "Create", { adding = false }) { ctl.createProject(it); adding = false }
    renaming?.let { p -> NameDialog("Rename project", p.name, "Save", { renaming = null }) { ctl.renameProject(p.id, it); renaming = null } }
    deleting?.let { p ->
        val n = data.issues.count { it.projectId == p.id }
        AlertDialog(
            onDismissRequest = { deleting = null },
            title = { Text("Delete “${p.name}”?", style = text(18.sp, FontWeight.SemiBold)) },
            text = { Text(if (n > 0) "This also deletes its $n item${if (n == 1) "" else "s"} from the synced database." else "The project is empty.", style = text(14.sp, color = Slate.s600)) },
            confirmButton = { TextButton({ ctl.deleteProject(p.id); deleting = null }) { Text("Delete", style = text(14.sp, FontWeight.SemiBold, Color(0xFFBE123C))) } },
            dismissButton = { TextButton({ deleting = null }) { Text("Cancel", style = text(14.sp, FontWeight.Medium, Slate.s600)) } },
            containerColor = Color.White,
        )
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ProjectChip(p: Project, active: Boolean, count: Int, onClick: () -> Unit, onDot: () -> Unit, onLong: () -> Unit) {
    val th = themeFor(p.colorKey)
    val shape = RoundedCornerShape(12.dp)
    Row(
        Modifier
            .then(if (active) Modifier.shadow(10.dp, shape, ambientColor = th.accent, spotColor = th.accent.copy(alpha = 0.5f)) else Modifier)
            .height(36.dp)
            .background(if (active) th.tintBg else Color.White, shape)
            .border(1.dp, if (active) th.tintBorder else Slate.s200, shape)
            .combinedClickable(interactionSource = null, indication = null, onClick = onClick, onLongClick = onLong)
            .padding(start = 6.dp, end = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(32.dp).combinedClickable(interactionSource = null, indication = null, onClick = onDot, onLongClick = onLong), contentAlignment = Alignment.Center) {
            Box(Modifier.size(16.dp).background(th.accent, CircleShape))
        }
        Text(p.name, style = text(13.sp, if (active) FontWeight.SemiBold else FontWeight.Normal, if (active) th.accentDark else Slate.s600), maxLines = 1)
        Spacer(Modifier.width(6.dp))
        Text(count.toString(), style = mono(11.sp, if (active) th.accentDark else Slate.s600).copy(fontWeight = FontWeight.Medium), modifier = Modifier.graphicsLayer { alpha = 0.6f })
    }
}

@Composable
private fun NameDialog(title: String, initial: String, action: String, onDismiss: () -> Unit, onDone: (String) -> Unit) {
    var name by remember { mutableStateOf(initial) }
    val focus = remember { FocusRequester() }
    val th = LocalTheme.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title, style = text(18.sp, FontWeight.SemiBold)) },
        text = {
            BasicTextField(
                name, { name = it }, singleLine = true,
                textStyle = text(15.sp, FontWeight.Medium),
                cursorBrush = SolidColor(th.accent),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { if (name.isNotBlank()) onDone(name) }),
                modifier = Modifier.fillMaxWidth().focusRequester(focus)
                    .border(1.dp, Slate.s200, RoundedCornerShape(10.dp)).padding(12.dp),
            )
            LaunchedEffect(Unit) { focus.requestFocus() }
        },
        confirmButton = { TextButton({ if (name.isNotBlank()) onDone(name) }) { Text(action, style = text(14.sp, FontWeight.SemiBold, th.accentDark)) } },
        dismissButton = { TextButton(onDismiss) { Text("Cancel", style = text(14.sp, FontWeight.Medium, Slate.s600)) } },
        containerColor = Color.White,
    )
}

// ================================================================== header

@Composable
private fun Header(project: Project, data: AppData, sync: SyncState, onCompleted: () -> Unit, onRefresh: () -> Unit) {
    val completed = data.issues.count { it.projectId == project.id && it.completed }
    val pending = data.outbox.size
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 16.dp).drawBehind {
            drawLine(Slate.s200.copy(alpha = 0.8f), Offset(0f, size.height), Offset(size.width, size.height), 1.dp.toPx())
        }.padding(bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val offlineAlpha by animateFloatAsState(if (sync == SyncState.Offline) 1f else 0f, tween(220), label = "offline")
        Box(Modifier.weight(1f).graphicsLayer { alpha = offlineAlpha }) {
            if (offlineAlpha > 0f) {
                Pill(onRefresh, height = 26.dp, hPad = 8.dp) {
                    Ico(Icons.cloudOff, 13.dp, Slate.s500)
                    PillText(if (pending > 0) "Offline · $pending saved locally" else "Offline", Slate.s500, 11.5.sp, FontWeight.Medium)
                }
            }
        }
        Pill(onCompleted, bg = Slate.s100, height = 32.dp, hPad = 12.dp, pressScale = 0.94f) {
            Ico(Icons.circleCheck, 14.dp, Slate.s600)
            PillText("Completed", Slate.s600, 12.sp)
            Text(completed.toString(), style = mono(11.5.sp, Slate.s600), modifier = Modifier.graphicsLayer { alpha = 0.8f })
            Ico(Icons.chevronDown, 14.dp, Slate.s600)
        }
    }
}

// ================================================================== list

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun IssueList(
    data: AppData, ctl: Controller, extracting: Set<String>,
    onPreview: (String) -> Unit, onDelete: (String) -> Unit,
    refreshing: Boolean, onRefresh: () -> Unit, modifier: Modifier,
) {
    val project = data.activeProject
    val main = remember(data.issues, project.id) { Logic.visibleMain(data.issues.filter { it.projectId == project.id }) }
    val listState = rememberLazyListState()
    var draggingId by remember { mutableStateOf<String?>(null) }
    var dragOffset by remember { mutableFloatStateOf(0f) }
    val view = LocalView.current
    var userRefresh by remember { mutableStateOf(false) }
    LaunchedEffect(refreshing) { if (!refreshing) userRefresh = false }

    LaunchedEffect(project.id) { listState.scrollToItem(0) }

    PullToRefreshBox(
        isRefreshing = userRefresh && refreshing,
        onRefresh = { userRefresh = true; onRefresh() },
        modifier = modifier,
    ) {
        if (main.isEmpty()) {
            Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
                Text(
                    "No open items in ${project.name} yet — add a task below or share one from any app.",
                    style = text(13.5.sp, color = Slate.s400), textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                )
            }
            return@PullToRefreshBox
        }
        LazyColumn(state = listState, contentPadding = PaddingValues(horizontal = 16.dp, vertical = 6.dp), modifier = Modifier.fillMaxSize()) {
            itemsIndexed(main, key = { _, i -> i.id }) { idx, issue ->
                val dragging = draggingId == issue.id
                val lift by animateFloatAsState(if (dragging) 1.02f else 1f, Springs.row, label = "lift")
                Box(
                    Modifier
                        .zIndex(if (dragging) 1f else 0f)
                        .then(if (dragging) Modifier else Modifier.animateItem())
                        .graphicsLayer {
                            translationY = if (dragging) dragOffset else 0f
                            scaleX = lift; scaleY = lift
                        }
                        .pointerInput(issue.id) {
                            detectDragGesturesAfterLongPress(
                                onDragStart = {
                                    view.heavy(); ctl.dragStart()
                                    draggingId = issue.id; dragOffset = 0f
                                },
                                onDrag = { change, amount ->
                                    change.consume()
                                    dragOffset += amount.y
                                    val infos = listState.layoutInfo.visibleItemsInfo
                                    val cur = infos.firstOrNull { it.key == issue.id } ?: return@detectDragGesturesAfterLongPress
                                    val center = cur.offset + dragOffset + cur.size / 2f
                                    val target = infos.firstOrNull { it.key != issue.id && center > it.offset && center < it.offset + it.size }
                                    if (target != null) {
                                        dragOffset -= (target.offset - cur.offset)
                                        view.tick()
                                        ctl.dragOver(issue.id, target.key as String)
                                    }
                                },
                                onDragEnd = { draggingId = null; dragOffset = 0f; ctl.dragEnd(issue.id) },
                                onDragCancel = { draggingId = null; dragOffset = 0f; ctl.dragEnd(issue.id) },
                            )
                        }
                ) {
                    SwipeRow(issue, onDone = { view.confirm(); ctl.setStatus(issue.id, Status.COMPLETED) }, onDelete = { onDelete(issue.id) }) {
                        IssueRow(
                            issue, ctl, extracting = issue.id in extracting, isLast = idx == main.lastIndex, onPreview = onPreview,
                            modifier = Modifier.background(if (issue.status == Status.IN_WORK) Color.Transparent else Color.White),
                        )
                    }
                }
            }
        }
    }
}

/** Swipe right = Done (emerald), swipe left = delete (with Undo). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SwipeRow(issue: Issue, onDone: () -> Unit, onDelete: () -> Unit, content: @Composable () -> Unit) {
    val state = rememberSwipeToDismissBoxState(
        confirmValueChange = { v ->
            when (v) {
                SwipeToDismissBoxValue.StartToEnd -> { onDone(); false }
                SwipeToDismissBoxValue.EndToStart -> { onDelete(); true }
                else -> false
            }
        },
        positionalThreshold = { it * 0.35f },
    )
    SwipeToDismissBox(
        state,
        enableDismissFromStartToEnd = issue.status != Status.COMPLETED,
        backgroundContent = {
            val dir = state.dismissDirection
            val (bg, icon, label, align) = when (dir) {
                SwipeToDismissBoxValue.StartToEnd -> Quad(Slate.e600, Icons.check, "Done", Alignment.CenterStart)
                SwipeToDismissBoxValue.EndToStart -> Quad(Color(0xFFE11D48), Icons.trash, "Delete", Alignment.CenterEnd)
                else -> Quad(Color.Transparent, Icons.check, "", Alignment.Center)
            }
            Box(Modifier.fillMaxSize().padding(vertical = 4.dp).background(bg, RoundedCornerShape(14.dp)).padding(horizontal = 18.dp), contentAlignment = align) {
                if (label.isNotEmpty()) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Ico(icon, 16.dp, Color.White); PillText(label, Color.White, 13.sp)
                }
            }
        },
    ) { content() }
}

private data class Quad<A, B, C, D>(val a: A, val b: B, val c: C, val d: D)

// ================================================================== composer

@Composable
private fun Composer(ctl: Controller, modifier: Modifier) {
    val th = LocalTheme.current
    val ctx = LocalContext.current
    val view = LocalView.current
    var value by rememberSaveable { mutableStateOf("") }
    var idea by rememberSaveable { mutableStateOf(false) }
    val staged = remember { mutableStateListOf<String>() }
    var uploading by remember { mutableIntStateOf(0) }
    val focus = remember { FocusRequester() }
    val len = value.trim().length
    val prompt = Logic.isPromptMode(value)
    val typing = len > 0 || staged.isNotEmpty()
    var wasPrompt by remember { mutableStateOf(false) }
    LaunchedEffect(prompt) { if (prompt != wasPrompt && len > 0) Sound.play(if (prompt) "drop" else "drag_start"); wasPrompt = prompt }

    fun stage(uris: List<Uri>) {
        if (uris.isEmpty()) return
        Sound.play("add")
        uploading += uris.size
        ctl.prepareImages(ctx, uris) { url -> uploading--; if (url != null) staged.add(url) }
    }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickMultipleVisualMedia(8)) { stage(it) }

    // Text/images shared from other apps, and the "New task" shortcut
    val share by ctl.pendingShare.collectAsState()
    LaunchedEffect(share) {
        share?.let { s ->
            if (s.text.isNotBlank()) value = if (value.isBlank()) s.text else value + "\n" + s.text
            stage(s.images)
            ctl.pendingShare.value = null
            focus.requestFocus()
        }
    }
    val focusTick by ctl.focusComposer.collectAsState()
    LaunchedEffect(focusTick) { if (focusTick > 0) focus.requestFocus() }

    fun submit() {
        if ((len == 0 && staged.isEmpty()) || uploading > 0) return
        view.confirm()
        ctl.addIssue(value, if (idea) Status.BACKLOG else Status.TASK, staged.toList())
        value = ""; staged.clear()
    }

    Column(
        modifier.fillMaxWidth().background(Color.White).drawBehind {
            drawLine(Slate.s200.copy(alpha = 0.8f), Offset(0f, 0f), Offset(size.width, 0f), 1.dp.toPx())
        }.padding(horizontal = 12.dp, vertical = 6.dp),
    ) {
        if (staged.isNotEmpty() || uploading > 0) {
            Row(Modifier.padding(start = 36.dp, top = 4.dp, bottom = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                staged.forEachIndexed { k, src -> StagedThumb(src, 60) { staged.removeAt(k) } }
                repeat(uploading) { UploadingThumb(60) }
            }
        }
        Row(verticalAlignment = Alignment.Bottom) {
            // + / submit
            val plusBg = when { !typing -> Color.Transparent; prompt -> th.accent; idea -> Slate.s600; else -> Slate.s900 }
            Pill(::submit, bg = plusBg, dashed = if (typing) null else Slate.s300, lip = typing, height = 30.dp, hPad = 7.dp, pressScale = 0.86f, enabled = uploading == 0 || !typing) {
                Ico(Icons.plus, 16.dp, if (typing) Color.White else Slate.s400)
            }
            Spacer(Modifier.width(8.dp))
            Box(Modifier.weight(1f).padding(vertical = 9.dp)) {
                if (value.isEmpty()) Text(
                    if (idea) "Add a backlog idea…" else "Add a task, prompt, or image…",
                    style = text(15.sp, FontWeight.Medium, Slate.s400), maxLines = 1,
                )
                BasicTextField(
                    value, { value = it },
                    textStyle = text(15.sp, FontWeight.Medium, Slate.s900, 21.sp),
                    cursorBrush = SolidColor(th.accent),
                    maxLines = 7,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send, capitalization = androidx.compose.ui.text.input.KeyboardCapitalization.Sentences),
                    keyboardActions = KeyboardActions(onSend = { submit() }),
                    modifier = Modifier.fillMaxWidth().heightIn(min = 22.dp).focusRequester(focus),
                )
            }
            Pill({ picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) }, bg = if (staged.isNotEmpty()) Slate.s200 else Color.Transparent, height = 30.dp, hPad = 7.dp) {
                Ico(Icons.image, 15.dp, if (staged.isNotEmpty()) Slate.s800 else Slate.s400)
            }
            Pill({ view.tick(); idea = !idea }, bg = if (idea) Slate.s200.copy(alpha = 0.9f) else Color.Transparent, border = if (idea) Slate.s300 else null, height = 30.dp, hPad = 9.dp) {
                Ico(Icons.lightbulb, 13.dp, if (idea) Slate.s800 else Slate.s400)
                PillText("Idea", if (idea) Slate.s800 else Slate.s400, 11.5.sp, FontWeight.Medium)
            }
            // Mode indicator: FileText (<100 chars) vs Terminal (prompt mode, Gemini title)
            AnimatedVisibility(len > 0, enter = scaleIn(initialScale = 0.6f) + fadeIn(), exit = scaleOut(targetScale = 0.6f) + fadeOut()) {
                Box(Modifier.padding(start = 4.dp, bottom = 5.dp).size(30.dp).background(if (prompt) th.tintHover else Slate.s100, CircleShape), contentAlignment = Alignment.Center) {
                    AnimatedContent(prompt, transitionSpec = {
                        (scaleIn(initialScale = 0.35f) + fadeIn()) togetherWith (scaleOut(targetScale = 0.35f) + fadeOut())
                    }, label = "mode") { p ->
                        Ico(if (p) Icons.terminal else Icons.fileText, 15.dp, if (p) th.accent else Slate.s400)
                    }
                }
            }
        }
    }
}

// ================================================================== completed sheet + lightbox

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CompletedSheet(data: AppData, ctl: Controller, extracting: Set<String>, onPreview: (String) -> Unit, onDismiss: () -> Unit) {
    val project = data.activeProject
    val done = data.issues.filter { it.projectId == project.id && it.completed }
    ModalBottomSheet(onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = false), containerColor = Color.White) {
        AnimatedProjectTheme(project.colorKey) {
            Column(Modifier.padding(horizontal = 16.dp)) {
                Row(Modifier.fillMaxWidth().padding(bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("Completed problems (${done.size})", style = text(13.sp, FontWeight.SemiBold, Slate.s500), modifier = Modifier.weight(1f))
                    Text("Tap Restore to bring one back", style = text(11.5.sp, color = Slate.s400))
                }
                if (done.isEmpty()) {
                    Text("No completed problems yet.", style = text(13.5.sp, color = Slate.s400), modifier = Modifier.padding(vertical = 32.dp).align(Alignment.CenterHorizontally))
                } else {
                    LazyColumn(Modifier.fillMaxWidth().navigationBarsPadding()) {
                        itemsIndexed(done, key = { _, i -> i.id }) { idx, issue ->
                            IssueRow(issue, ctl, issue.id in extracting, idx == done.lastIndex, onPreview, Modifier.animateItem())
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun Lightbox(src: String, onClose: () -> Unit) {
    var scale by remember { mutableFloatStateOf(1f) }
    var offset by remember { mutableStateOf(Offset.Zero) }
    val tf = rememberTransformableState { zoom, pan, _ ->
        scale = (scale * zoom).coerceIn(1f, 5f)
        offset = if (scale == 1f) Offset.Zero else offset + pan
    }
    Dialog(onClose, DialogProperties(usePlatformDefaultWidth = false)) {
        Box(
            Modifier.fillMaxSize().background(Slate.s950.copy(alpha = 0.9f))
                .pointerInput(Unit) { detectTapGestures(onTap = { onClose() }, onDoubleTap = { scale = if (scale > 1f) 1f else 2.5f; offset = Offset.Zero }) },
            contentAlignment = Alignment.Center,
        ) {
            AsyncImage(
                ImageTools.model(src), "Attachment preview", contentScale = ContentScale.Fit,
                modifier = Modifier.padding(16.dp).clip(RoundedCornerShape(16.dp)).transformable(tf)
                    .graphicsLayer { scaleX = scale; scaleY = scale; translationX = offset.x; translationY = offset.y },
            )
            Box(
                Modifier.align(Alignment.TopEnd).statusBarsPadding().padding(16.dp).size(40.dp)
                    .background(Color.White.copy(alpha = 0.15f), CircleShape)
                    .pointerInput(Unit) { detectTapGestures { onClose() } },
                contentAlignment = Alignment.Center,
            ) { Ico(Icons.x, 20.dp, Color.White) }
        }
    }
}

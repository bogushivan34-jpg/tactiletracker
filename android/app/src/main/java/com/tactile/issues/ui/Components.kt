package com.tactile.issues.ui

import android.view.HapticFeedbackConstants
import android.view.View
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

fun View.tick() = performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
fun View.confirm() = performHapticFeedback(if (android.os.Build.VERSION.SDK_INT >= 30) HapticFeedbackConstants.CONFIRM else HapticFeedbackConstants.VIRTUAL_KEY)
fun View.heavy() = performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)

fun text(size: TextUnit, weight: FontWeight = FontWeight.Normal, color: Color = Slate.s900, lineHeight: TextUnit = TextUnit.Unspecified) =
    TextStyle(fontFamily = Jakarta, fontSize = size, fontWeight = weight, color = color, lineHeight = lineHeight)

fun mono(size: TextUnit, color: Color) = TextStyle(fontFamily = JetMono, fontSize = size, fontWeight = FontWeight.SemiBold, color = color)

/** Dashed rounded outline (Tailwind border-dashed). */
fun Modifier.dashedBorder(color: Color, radius: Dp, width: Dp = 1.5.dp) = drawBehind {
    val w = width.toPx()
    drawRoundRect(
        color = color, topLeft = Offset(w / 2, w / 2),
        size = androidx.compose.ui.geometry.Size(size.width - w, size.height - w),
        cornerRadius = CornerRadius(radius.toPx()),
        style = Stroke(width = w, pathEffect = PathEffect.dashPathEffect(floatArrayOf(3.3f * w, 2.4f * w))),
    )
}

/**
 * Tactile pill: springy press scale (motion stiffness 550/damping 24), a 2dp "lip" shadow that disappears
 * while pressed (the web's .tactile-btn), optional border or dashed outline. The touch target is padded
 * to >= 40dp tall without changing the visual size.
 */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun Pill(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    bg: Color = Color.Transparent,
    border: Color? = null,
    dashed: Color? = null,
    lip: Boolean = false,
    height: Dp = 26.dp,
    hPad: Dp = 10.dp,
    pressScale: Float = 0.9f,
    enabled: Boolean = true,
    onLongClick: (() -> Unit)? = null,
    content: @Composable RowScope.() -> Unit,
) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) pressScale else 1f, if (pressed) androidx.compose.animation.core.tween(70) else Springs.press, label = "press")
    val shape = RoundedCornerShape(height / 2)
    val lipColor = Slate.s900.copy(alpha = 0.12f)
    Box(
        modifier
            .combinedClickable(interactionSource = source, indication = null, enabled = enabled, onLongClick = onLongClick, onClick = onClick)
            .padding(vertical = ((40.dp - height) / 2).coerceAtLeast(0.dp)),
        contentAlignment = Alignment.Center,
    ) {
        Row(
            Modifier
                .graphicsLayer { scaleX = scale; scaleY = scale; alpha = if (enabled) 1f else 0.3f }
                .drawBehind {
                    if (lip && !pressed) drawRoundRect(lipColor, topLeft = Offset(0f, 2.dp.toPx()), cornerRadius = CornerRadius(size.height / 2))
                }
                .height(height)
                .background(bg, shape)
                .then(if (border != null) Modifier.border(1.dp, border, shape) else Modifier)
                .then(if (dashed != null) Modifier.dashedBorder(dashed, height / 2) else Modifier)
                .padding(PaddingValues(horizontal = hPad)),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(5.dp),
            content = content,
        )
    }
}

@Composable
fun Ico(v: ImageVector, size: Dp, tint: Color, modifier: Modifier = Modifier) =
    Icon(v, contentDescription = null, tint = tint, modifier = modifier.size(size))

@Composable
fun PillText(t: String, color: Color, size: TextUnit = 11.5.sp, weight: FontWeight = FontWeight.SemiBold) =
    Text(t, style = text(size, weight, color), maxLines = 1)

/** The running indicator: three dots in a sine wave (0.85s, staggered 0.16s), like .in-work-dot. */
@Composable
fun RunningDots(color: Color) {
    val t = androidx.compose.animation.core.rememberInfiniteTransition(label = "dots")
    Row(horizontalArrangement = Arrangement.spacedBy(2.dp), verticalAlignment = Alignment.CenterVertically) {
        for (k in 0..2) {
            val p by t.animateFloat(
                0f, 1f,
                androidx.compose.animation.core.infiniteRepeatable(
                    androidx.compose.animation.core.tween(425, easing = androidx.compose.animation.core.FastOutSlowInEasing),
                    androidx.compose.animation.core.RepeatMode.Reverse,
                    androidx.compose.animation.core.StartOffset(k * 160),
                ), label = "dot$k"
            )
            Box(
                Modifier
                    .size(3.5.dp)
                    .graphicsLayer {
                        translationY = (-1.8f + 3.6f * p) * density
                        val s = 0.88f + 0.24f * p
                        scaleX = s; scaleY = s
                        alpha = 0.45f + 0.55f * p
                    }
                    .background(color, RoundedCornerShape(50))
            )
        }
    }
}

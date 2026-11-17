package com.tactile.issues.ui

// Design tokens copied from src/lib/projectThemes.ts + Tailwind's slate/emerald palette.

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.SpringSpec
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.tactile.issues.R
import kotlin.math.sqrt

object Slate {
    val s50 = Color(0xFFF8FAFC); val s100 = Color(0xFFF1F5F9); val s200 = Color(0xFFE2E8F0); val s300 = Color(0xFFCBD5E1)
    val s400 = Color(0xFF94A3B8); val s500 = Color(0xFF64748B); val s600 = Color(0xFF475569); val s700 = Color(0xFF334155)
    val s800 = Color(0xFF1E293B); val s900 = Color(0xFF0F172A); val s950 = Color(0xFF020617)
    val e50 = Color(0xFFECFDF5); val e200 = Color(0xFFA7F3D0); val e600 = Color(0xFF059669); val e700 = Color(0xFF047857)
}

@Immutable
data class ProjectTheme(
    val key: String, val label: String,
    val accent: Color, val accentStrong: Color, val accentDark: Color,
    val tintBg: Color, val tintBgSubtle: Color, val tintBorder: Color, val tintDrawerBorder: Color,
    val tintHover: Color, val tintMuted: Color, val selection: Color,
)

private fun c(hex: Long) = Color(0xFF000000 or hex)

val Themes: Map<String, ProjectTheme> = listOf(
    ProjectTheme("coral", "Red-Orange", c(0xFF4F28), c(0xE03E1A), c(0xC23210), c(0xFFF5F0), c(0xFFF9F6), c(0xFFC4B0), c(0xFFD8CC), c(0xFFF0EC), Color(154, 52, 18, 184), c(0xFFE4DE)),
    ProjectTheme("blue", "Ocean Blue", c(0x2563EB), c(0x1D4ED8), c(0x1E40AF), c(0xEFF6FF), c(0xF5F9FF), c(0xBFDBFE), c(0xDBEAFE), c(0xE0EEFF), Color(30, 64, 175, 184), c(0xDBEAFE)),
    ProjectTheme("emerald", "Mint Emerald", c(0x059669), c(0x047857), c(0x065F46), c(0xECFDF5), c(0xF4FCF8), c(0xA7F3D0), c(0xD1FAE5), c(0xDFF9EE), Color(6, 95, 70, 184), c(0xD1FAE5)),
    ProjectTheme("violet", "Iris Violet", c(0x7C3AED), c(0x6D28D9), c(0x5B21B6), c(0xF5F3FF), c(0xF9F8FF), c(0xDDD6FE), c(0xEDE9FE), c(0xEDE9FE), Color(91, 33, 182, 184), c(0xEDE9FE)),
    ProjectTheme("amber", "Warm Amber", c(0xD97706), c(0xB45309), c(0x92400E), c(0xFFFBEB), c(0xFFFCF2), c(0xFDE68A), c(0xFEF3C7), c(0xFEF3C7), Color(146, 64, 14, 191), c(0xFEF3C7)),
    ProjectTheme("rose", "Berry Rose", c(0xE11D48), c(0xBE123C), c(0x9F1239), c(0xFFF1F2), c(0xFFF6F7), c(0xFECDD3), c(0xFFE4E6), c(0xFFE4E6), Color(159, 18, 57, 184), c(0xFFE4E6)),
    ProjectTheme("teal", "Lagoon Teal", c(0x0D9488), c(0x0F766E), c(0x115E59), c(0xF0FDFA), c(0xF5FEFC), c(0x99F6E4), c(0xCCFBF1), c(0xCCFBF1), Color(17, 94, 89, 184), c(0xCCFBF1)),
).associateBy { it.key }

fun themeFor(key: String) = Themes[key] ?: Themes.getValue("coral")

val LocalTheme = staticCompositionLocalOf { themeFor("coral") }

/** Animates every token when the active project changes (220 ms, like the web's color transition). */
@Composable
fun AnimatedProjectTheme(key: String, content: @Composable () -> Unit) {
    val t = themeFor(key)
    val spec = tween<Color>(220)
    @Composable fun a(c: Color) = animateColorAsState(c, spec, label = "theme").value
    val animated = t.copy(
        accent = a(t.accent), accentStrong = a(t.accentStrong), accentDark = a(t.accentDark), tintBg = a(t.tintBg),
        tintBgSubtle = a(t.tintBgSubtle), tintBorder = a(t.tintBorder), tintDrawerBorder = a(t.tintDrawerBorder),
        tintHover = a(t.tintHover), tintMuted = a(t.tintMuted), selection = a(t.selection),
    )
    CompositionLocalProvider(LocalTheme provides animated, content = content)
}

val Jakarta = FontFamily(
    Font(R.font.plus_jakarta_sans_regular, FontWeight.Normal),
    Font(R.font.plus_jakarta_sans_medium, FontWeight.Medium),
    Font(R.font.plus_jakarta_sans_semibold, FontWeight.SemiBold),
    Font(R.font.plus_jakarta_sans_bold, FontWeight.Bold),
)
val JetMono = FontFamily(
    Font(R.font.jetbrains_mono_regular, FontWeight.Normal),
    Font(R.font.jetbrains_mono_medium, FontWeight.Medium),
    Font(R.font.jetbrains_mono_semibold, FontWeight.SemiBold),
)

/** motion's {stiffness, damping, mass} -> Compose spring (dampingRatio = d / (2*sqrt(k*m))). */
fun <T> motionSpring(stiffness: Float, damping: Float, mass: Float = 1f): SpringSpec<T> {
    val ratio = damping / (2f * sqrt(stiffness * mass))
    return spring(dampingRatio = ratio, stiffness = stiffness / mass)
}

object Springs {
    val press = motionSpring<Float>(550f, 24f)
    val row = motionSpring<Float>(520f, 34f, 0.7f)
    val drawer = motionSpring<Float>(460f, 34f)
    val drawerSize = spring<androidx.compose.ui.unit.IntSize>(dampingRatio = 0.79f, stiffness = 460f)
    val bouncy = spring<Float>(Spring.DampingRatioMediumBouncy, Spring.StiffnessMedium)
}

// ---------------- Lucide icons (24x24 stroke paths) ----------------

object Icons {
    private fun stroke(name: String, d: String, width: Float = 2f) = ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).addPath(
        pathData = PathParser().parsePathString(d).toNodes(),
        stroke = SolidColor(Color.Black), strokeLineWidth = width,
        strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round,
    ).build()

    private fun fill(name: String, d: String) = ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).addPath(
        pathData = PathParser().parsePathString(d).toNodes(), fill = SolidColor(Color.Black),
    ).build()

    val plus by lazy { stroke("plus", "M5 12h14 M12 5v14", 2.5f) }
    val x by lazy { stroke("x", "M18 6 6 18 M6 6l12 12") }
    val check by lazy { stroke("check", "M20 6 9 17l-5-5", 2.5f) }
    val chevronDown by lazy { stroke("chev", "M6 9l6 6 6-6") }
    val message by lazy { stroke("msg", "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z") }
    val image by lazy { stroke("img", "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M7 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0z M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21", 2.1f) }
    val lightbulb by lazy { stroke("bulb", "M15 14c0.2-1 0.7-1.7 1.5-2.5 1-0.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 0.2 2.2 1.5 3.5 0.7 0.7 1.3 1.5 1.5 2.5 M9 18h6 M10 22h4", 2.1f) }
    val circleCheck by lazy { stroke("cc", "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0z M9 12l2 2 4-4", 2.1f) }
    val terminal by lazy { stroke("term", "M4 17l6-6-6-6 M12 19h8", 2.3f) }
    val fileText by lazy { stroke("file", "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z M14 2v4a2 2 0 0 0 2 2h4 M10 9H8 M16 13H8 M16 17H8", 2.1f) }
    val loader by lazy { stroke("loader", "M21 12a9 9 0 1 1-6.219-8.56") }
    val send by lazy { stroke("send", "M9 10l-5 5 5 5 M20 4v7a4 4 0 0 1-4 4H4") }
    val rotate by lazy { stroke("rot", "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5", 2.2f) }
    val pencil by lazy { stroke("pen", "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-0.5 0.83l-1.321 4.352a0.5 0.5 0 0 0 0.623 0.622l4.353-1.32a2 2 0 0 0 0.83-0.497z M15 5l4 4") }
    val cloudOff by lazy { stroke("cloud", "M2 2l20 20 M5.782 5.782A7 7 0 0 0 9 19h8.5a4.5 4.5 0 0 0 1.307-0.193 M21.532 16.5A4.5 4.5 0 0 0 17.5 10h-1.79A7.008 7.008 0 0 0 10 5.07") }
    val trash by lazy { stroke("trash", "M3 6h18 M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6 M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2") }
    val volume by lazy { stroke("vol", "M11 5L6 9H2v6h4l5 4V5z M15.54 8.46a5 5 0 0 1 0 7.07") }
    val volumeOff by lazy { stroke("voloff", "M11 5L6 9H2v6h4l5 4V5z M22 9l-6 6 M16 9l6 6") }
    val play by lazy { fill("play", "M6 3L20 12 6 21z") }
}

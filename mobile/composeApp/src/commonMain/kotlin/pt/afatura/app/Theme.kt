package pt.afatura.app

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

object Brand {
    val Teal = Color(0xFF0F766E)
    val TealLight = Color(0xFF14B8A6)
    val Gradient = Brush.linearGradient(listOf(Teal, TealLight))
}

/** Semantic colors that change with the theme (light / dark). */
data class AppColors(
    val surface: Color, val line: Color, val muted: Color,
    val chip: Color, val chipFg: Color, val userBg: Color, val userFg: Color,
    val ok: Color, val okSoft: Color, val warn: Color, val warnSoft: Color,
    val bad: Color, val badSoft: Color, val info: Color, val infoSoft: Color,
)

private val LightApp = AppColors(
    surface = Color.White, line = Color(0xFFE2E8F0), muted = Color(0xFF64748B),
    chip = Color(0xFFCCFBF1), chipFg = Color(0xFF115E59), userBg = Color(0xFFE0E7FF), userFg = Color(0xFF3730A3),
    ok = Color(0xFF15803D), okSoft = Color(0xFFDCFCE7), warn = Color(0xFFB45309), warnSoft = Color(0xFFFEF3C7),
    bad = Color(0xFFB91C1C), badSoft = Color(0xFFFEE2E2), info = Color(0xFF0369A1), infoSoft = Color(0xFFE0F2FE),
)

private val DarkApp = AppColors(
    surface = Color(0xFF111A2B), line = Color(0xFF1F2B40), muted = Color(0xFF94A3B8),
    chip = Color(0xFF134E4A), chipFg = Color(0xFF99F6E4), userBg = Color(0xFF312E81), userFg = Color(0xFFC7D2FE),
    ok = Color(0xFF4ADE80), okSoft = Color(0x264ADE80), warn = Color(0xFFFBBF24), warnSoft = Color(0x26FBBF24),
    bad = Color(0xFFF87171), badSoft = Color(0x26F87171), info = Color(0xFF38BDF8), infoSoft = Color(0x2638BDF8),
)

val LocalAppColors = staticCompositionLocalOf { LightApp }

/** Theme-aware semantic colors: `C.line`, `C.muted`, `C.ok`… */
val C: AppColors
    @Composable @ReadOnlyComposable get() = LocalAppColors.current

private val LightScheme = lightColorScheme(
    primary = Brand.Teal, onPrimary = Color.White,
    primaryContainer = Color(0xFFCCFBF1), onPrimaryContainer = Color(0xFF134E4A),
    secondary = Brand.TealLight,
    background = Color(0xFFF6F7F9), surface = Color.White, surfaceVariant = Color(0xFFF1F5F9),
    onBackground = Color(0xFF0F172A), onSurface = Color(0xFF0F172A), onSurfaceVariant = Color(0xFF64748B),
    outline = Color(0xFFE2E8F0), outlineVariant = Color(0xFFE2E8F0), error = Color(0xFFB91C1C),
)

private val DarkScheme = darkColorScheme(
    primary = Color(0xFF2DD4BF), onPrimary = Color(0xFF042F2E),
    primaryContainer = Color(0xFF134E4A), onPrimaryContainer = Color(0xFF99F6E4),
    secondary = Brand.TealLight,
    background = Color(0xFF0B1220), surface = Color(0xFF111A2B), surfaceVariant = Color(0xFF1A2538),
    surfaceContainer = Color(0xFF111A2B), surfaceContainerLow = Color(0xFF111A2B), surfaceContainerHigh = Color(0xFF1A2538),
    onBackground = Color(0xFFE2E8F0), onSurface = Color(0xFFE2E8F0), onSurfaceVariant = Color(0xFF94A3B8),
    outline = Color(0xFF334155), outlineVariant = Color(0xFF1F2B40), error = Color(0xFFF87171),
)

/** "system" follows the phone setting; "light"/"dark" force it. */
@Composable
fun AFaturaTheme(mode: String = "system", content: @Composable () -> Unit) {
    val dark = when (mode) { "dark" -> true; "light" -> false; else -> isSystemInDarkTheme() }
    CompositionLocalProvider(LocalAppColors provides if (dark) DarkApp else LightApp) {
        MaterialTheme(
            colorScheme = if (dark) DarkScheme else LightScheme,
            shapes = Shapes(
                small = RoundedCornerShape(10.dp),
                medium = RoundedCornerShape(16.dp),
                large = RoundedCornerShape(24.dp),
            ),
            content = content,
        )
    }
}

/** Brand mark: gradient rounded square with "A€". */
@Composable
fun Logo(size: Dp = 44.dp) {
    Box(
        Modifier.size(size).clip(RoundedCornerShape(size * 0.28f)).background(Brand.Gradient),
        contentAlignment = Alignment.Center,
    ) {
        Text("A€", color = Color.White, fontWeight = FontWeight.ExtraBold, fontSize = (size.value * 0.36f).sp)
    }
}

/** Initials avatar used for organizations and the user. */
@Composable
fun Avatar(text: String, size: Dp = 40.dp, bg: Color = C.chip, fg: Color = C.chipFg) {
    val initials = text.split(" ").filter { it.isNotBlank() }.take(2).joinToString("") { it.first().uppercase() }
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.3f)).background(bg), contentAlignment = Alignment.Center) {
        Text(initials.ifEmpty { "?" }, color = fg, fontWeight = FontWeight.Bold, fontSize = (size.value * 0.36f).sp)
    }
}

fun eur(v: Double?): String {
    if (v == null) return "—"
    val cents = kotlin.math.round(kotlin.math.abs(v) * 100).toLong()
    val units = (cents / 100).toString().reversed().chunked(3).joinToString(" ").reversed()
    return (if (v < 0) "-" else "") + units + "," + (cents % 100).toString().padStart(2, '0') + " €"
}

fun ptDate(iso: String?): String? = iso?.split("-")?.takeIf { it.size == 3 }?.let { "${it[2]}/${it[1]}/${it[0]}" }

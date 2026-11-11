package pt.afatura.app

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Text
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
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
    val TealSoft = Color(0xFFCCFBF1)
    val Ink = Color(0xFF0F172A)
    val Muted = Color(0xFF64748B)
    val Line = Color(0xFFE2E8F0)
    val Bg = Color(0xFFF6F7F9)
    val Ok = Color(0xFF15803D)
    val OkSoft = Color(0xFFDCFCE7)
    val Warn = Color(0xFFB45309)
    val WarnSoft = Color(0xFFFEF3C7)
    val Bad = Color(0xFFB91C1C)
    val BadSoft = Color(0xFFFEE2E2)
    val Info = Color(0xFF0369A1)
    val InfoSoft = Color(0xFFE0F2FE)
    val Gradient = Brush.linearGradient(listOf(Teal, TealLight))
}

private val Colors = lightColorScheme(
    primary = Brand.Teal,
    onPrimary = Color.White,
    primaryContainer = Brand.TealSoft,
    onPrimaryContainer = Color(0xFF134E4A),
    secondary = Brand.TealLight,
    background = Brand.Bg,
    surface = Color.White,
    surfaceVariant = Color(0xFFF1F5F9),
    onSurface = Brand.Ink,
    onSurfaceVariant = Brand.Muted,
    outline = Brand.Line,
    outlineVariant = Brand.Line,
    error = Brand.Bad,
)

@Composable
fun AFaturaTheme(content: @Composable () -> Unit) = MaterialTheme(
    colorScheme = Colors,
    shapes = Shapes(
        small = RoundedCornerShape(10.dp),
        medium = RoundedCornerShape(16.dp),
        large = RoundedCornerShape(24.dp),
    ),
    content = content,
)

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
fun Avatar(text: String, size: Dp = 40.dp, bg: Color = Brand.TealSoft, fg: Color = Color(0xFF115E59)) {
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

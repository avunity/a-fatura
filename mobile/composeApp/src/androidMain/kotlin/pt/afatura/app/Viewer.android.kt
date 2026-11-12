package pt.afatura.app

import android.content.ActivityNotFoundException
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.pdf.PdfRenderer
import android.os.ParcelFileDescriptor
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.core.content.FileProvider
import java.io.File

actual fun decodePreview(file: CapturedFile): ImageBitmap? = runCatching {
    if (file.mimeType == "application/pdf") {
        val tmp = File.createTempFile("preview", ".pdf").apply { writeBytes(file.bytes); deleteOnExit() }
        try {
            ParcelFileDescriptor.open(tmp, ParcelFileDescriptor.MODE_READ_ONLY).use { fd ->
                PdfRenderer(fd).use { pdf ->
                    if (pdf.pageCount == 0) return@runCatching null
                    pdf.openPage(0).use { page ->
                        val scale = 1400f / maxOf(page.width, page.height)
                        val bmp = Bitmap.createBitmap((page.width * scale).toInt(), (page.height * scale).toInt(), Bitmap.Config.ARGB_8888)
                        bmp.eraseColor(Color.WHITE)
                        page.render(bmp, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                        bmp.asImageBitmap()
                    }
                }
            }
        } finally { tmp.delete() }
    } else {
        // Downsample large photos to keep memory low
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(file.bytes, 0, file.bytes.size, bounds)
        var sample = 1
        while (maxOf(bounds.outWidth, bounds.outHeight) / sample > 2000) sample *= 2
        BitmapFactory.decodeByteArray(file.bytes, 0, file.bytes.size, BitmapFactory.Options().apply { inSampleSize = sample })?.asImageBitmap()
    }
}.getOrNull()

@Composable
actual fun rememberDocumentOpener(): (CapturedFile) -> Unit {
    val context = LocalContext.current
    return remember(context) {
        { file ->
            val dir = File(context.cacheDir, "captures").apply { mkdirs() }
            val safe = file.fileName.replace(Regex("[^A-Za-z0-9._-]"), "_").ifBlank { "documento" }
            val out = File(dir, "ver-$safe").apply { writeBytes(file.bytes) }
            val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", out)
            val intent = Intent(Intent.ACTION_VIEW).setDataAndType(uri, file.mimeType).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            try {
                context.startActivity(Intent.createChooser(intent, "Abrir documento").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
            } catch (_: ActivityNotFoundException) {
                Toast.makeText(context, "Nenhuma aplicação para abrir este documento", Toast.LENGTH_SHORT).show()
            }
        }
    }
}

@Composable
actual fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit) = BackHandler(enabled, onBack)

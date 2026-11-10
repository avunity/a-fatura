package pt.afatura.app

import android.net.Uri
import android.provider.OpenableColumns
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.core.content.FileProvider
import java.io.File

@Composable
actual fun rememberDocumentCapture(onResult: (CapturedFile?) -> Unit): DocumentCapture {
    val context = LocalContext.current
    val photoFile = remember { File(context.cacheDir, "captures").apply { mkdirs() }.let { File(it, "capture.jpg") } }
    val photoUri = remember { FileProvider.getUriForFile(context, "${context.packageName}.files", photoFile) }

    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        if (!ok) return@rememberLauncherForActivityResult onResult(null)
        val bytes = photoFile.readBytes()
        onResult(CapturedFile(bytes, "fatura-${System.currentTimeMillis()}.jpg", "image/jpeg"))
    }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri: Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult onResult(null)
        val resolver = context.contentResolver
        val mime = resolver.getType(uri) ?: "application/octet-stream"
        val name = resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0) else null
        } ?: "documento"
        val bytes = resolver.openInputStream(uri)?.use { it.readBytes() }
        onResult(bytes?.let { CapturedFile(it, name, mime) })
    }

    return remember {
        object : DocumentCapture {
            override fun takePhoto() = camera.launch(photoUri)
            override fun pickFile() = picker.launch(arrayOf("application/pdf", "image/*"))
        }
    }
}

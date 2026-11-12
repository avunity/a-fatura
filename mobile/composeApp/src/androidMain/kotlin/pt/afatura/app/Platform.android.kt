package pt.afatura.app

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.net.Uri
import android.provider.OpenableColumns
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.IntentSenderRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import androidx.core.content.FileProvider
import com.google.mlkit.vision.documentscanner.GmsDocumentScannerOptions
import com.google.mlkit.vision.documentscanner.GmsDocumentScanning
import com.google.mlkit.vision.documentscanner.GmsDocumentScanningResult
import java.io.File

private tailrec fun Context.findActivity(): ComponentActivity? = when (this) {
    is ComponentActivity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}

/**
 * "Digitalizar": Google ML Kit Document Scanner — finds the invoice edges, crops the background, fixes perspective and
 * offers automatic enhancement (shadow/stain removal, contrast). One page → JPEG; several pages → one PDF.
 * Falls back to the plain camera when Google Play services are not available.
 */
@Composable
actual fun rememberDocumentCapture(onResult: (CapturedFile?) -> Unit): DocumentCapture {
    val context = LocalContext.current
    val photoFile = remember { File(context.cacheDir, "captures").apply { mkdirs() }.let { File(it, "capture.jpg") } }
    val photoUri = remember { FileProvider.getUriForFile(context, "${context.packageName}.files", photoFile) }

    fun readUri(uri: Uri): ByteArray? = context.contentResolver.openInputStream(uri)?.use { it.readBytes() }
    val stamp = { System.currentTimeMillis() }

    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        if (!ok) return@rememberLauncherForActivityResult onResult(null)
        onResult(CapturedFile(photoFile.readBytes(), "fatura-${stamp()}.jpg", "image/jpeg"))
    }

    val scanner = remember {
        GmsDocumentScanning.getClient(
            GmsDocumentScannerOptions.Builder()
                .setScannerMode(GmsDocumentScannerOptions.SCANNER_MODE_FULL) // crop + filters + clean-up
                .setGalleryImportAllowed(true)
                .setPageLimit(40)
                .setResultFormats(GmsDocumentScannerOptions.RESULT_FORMAT_JPEG, GmsDocumentScannerOptions.RESULT_FORMAT_PDF)
                .build()
        )
    }

    val scanLauncher = rememberLauncherForActivityResult(ActivityResultContracts.StartIntentSenderForResult()) { res ->
        if (res.resultCode != Activity.RESULT_OK) return@rememberLauncherForActivityResult onResult(null)
        val result = GmsDocumentScanningResult.fromActivityResultIntent(res.data)
            ?: return@rememberLauncherForActivityResult onResult(null)
        val pages = result.pages.orEmpty()
        val file = if (pages.size == 1) {
            readUri(pages[0].imageUri)?.let { CapturedFile(it, "fatura-${stamp()}.jpg", "image/jpeg") }
        } else {
            result.pdf?.uri?.let(::readUri)?.let { CapturedFile(it, "fatura-${stamp()}.pdf", "application/pdf") }
        }
        onResult(file)
    }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri: Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult onResult(null)
        val resolver = context.contentResolver
        val mime = resolver.getType(uri) ?: "application/octet-stream"
        val name = resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
            if (c.moveToFirst()) c.getString(0) else null
        } ?: "documento"
        onResult(readUri(uri)?.let { CapturedFile(it, name, mime) })
    }

    return remember {
        object : DocumentCapture {
            override fun takePhoto() {
                val activity = context.findActivity()
                if (activity == null) { camera.launch(photoUri); return }
                scanner.getStartScanIntent(activity)
                    .addOnSuccessListener { sender -> scanLauncher.launch(IntentSenderRequest.Builder(sender).build()) }
                    .addOnFailureListener { camera.launch(photoUri) }
            }

            override fun pickFile() = picker.launch(arrayOf("application/pdf", "image/*"))
        }
    }
}

package pt.afatura.app

import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.ImageBitmap

/** Camera / file picker, implemented per platform. Results arrive through the callback given to [rememberDocumentCapture]. */
interface DocumentCapture {
    fun takePhoto()
    fun pickFile()
}

@Composable
expect fun rememberDocumentCapture(onResult: (CapturedFile?) -> Unit): DocumentCapture

/** Image for the in-app preview: the photo itself, or the first page of a PDF (where the platform can render it). Call off the main thread. */
expect fun decodePreview(file: CapturedFile): ImageBitmap?

/** Opens the document in the system viewer (zoom, all PDF pages, share). */
@Composable
expect fun rememberDocumentOpener(): (CapturedFile) -> Unit

/** System "back" (Android back gesture/button). No-op where the platform has none. */
@Composable
expect fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit)

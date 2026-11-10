package pt.afatura.app

import androidx.compose.runtime.Composable

/** Camera / file picker, implemented per platform. Results arrive through the callback given to [rememberDocumentCapture]. */
interface DocumentCapture {
    fun takePhoto()
    fun pickFile()
}

@Composable
expect fun rememberDocumentCapture(onResult: (CapturedFile?) -> Unit): DocumentCapture

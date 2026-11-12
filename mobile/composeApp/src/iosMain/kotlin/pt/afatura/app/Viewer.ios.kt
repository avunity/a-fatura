@file:OptIn(kotlinx.cinterop.ExperimentalForeignApi::class, kotlinx.cinterop.BetaInteropApi::class)

package pt.afatura.app

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.toComposeImageBitmap
import kotlinx.cinterop.addressOf
import kotlinx.cinterop.usePinned
import org.jetbrains.skia.Image
import platform.Foundation.NSData
import platform.Foundation.NSTemporaryDirectory
import platform.Foundation.NSURL
import platform.Foundation.create
import platform.Foundation.writeToURL
import platform.UIKit.UIApplication
import platform.UIKit.UIDocumentInteractionController
import platform.UIKit.UIDocumentInteractionControllerDelegateProtocol
import platform.UIKit.UIViewController
import platform.darwin.NSObject

actual fun decodePreview(file: CapturedFile): ImageBitmap? =
    if (file.mimeType == "application/pdf") null // iOS: o PDF abre no visualizador do sistema
    else runCatching { Image.makeFromEncoded(file.bytes).toComposeImageBitmap() }.getOrNull()

private fun rootController(): UIViewController? {
    var vc = UIApplication.sharedApplication.keyWindow?.rootViewController
    while (vc?.presentedViewController != null) vc = vc.presentedViewController
    return vc
}

private class PreviewDelegate : NSObject(), UIDocumentInteractionControllerDelegateProtocol {
    override fun documentInteractionControllerViewControllerForPreview(controller: UIDocumentInteractionController): UIViewController =
        rootController() ?: UIViewController()
}

private class Opener {
    val delegate = PreviewDelegate()
    var controller: UIDocumentInteractionController? = null // held strongly while the preview is open

    fun open(file: CapturedFile) {
        val safe = file.fileName.replace(Regex("[^A-Za-z0-9._-]"), "_").ifBlank { "documento" }
        val url = NSURL.fileURLWithPath(NSTemporaryDirectory() + "ver-" + safe)
        val data = file.bytes.usePinned { NSData.create(bytes = it.addressOf(0), length = file.bytes.size.toULong()) }
        data.writeToURL(url, true)
        val c = UIDocumentInteractionController.interactionControllerWithURL(url)
        c.delegate = delegate
        controller = c
        c.presentPreviewAnimated(true)
    }
}

@Composable
actual fun rememberDocumentOpener(): (CapturedFile) -> Unit {
    val opener = remember { Opener() }
    return remember { { file -> opener.open(file) } }
}

@Composable
actual fun PlatformBackHandler(enabled: Boolean, onBack: () -> Unit) { /* iOS: botão "voltar" no ecrã */ }

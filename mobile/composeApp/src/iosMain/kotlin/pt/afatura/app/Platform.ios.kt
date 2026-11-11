@file:OptIn(kotlinx.cinterop.ExperimentalForeignApi::class, kotlinx.cinterop.BetaInteropApi::class)

package pt.afatura.app

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import kotlinx.cinterop.addressOf
import kotlinx.cinterop.useContents
import kotlinx.cinterop.usePinned
import platform.CoreGraphics.CGRectMake
import platform.Foundation.NSData
import platform.Foundation.NSError
import platform.Foundation.NSMutableData
import platform.Foundation.NSDate
import platform.Foundation.NSURL
import platform.Foundation.dataWithContentsOfURL
import platform.Foundation.timeIntervalSince1970
import platform.UIKit.UIApplication
import platform.UIKit.UIDocumentPickerDelegateProtocol
import platform.UIKit.UIDocumentPickerViewController
import platform.UIKit.UIImage
import platform.UIKit.UIGraphicsBeginPDFContextToData
import platform.UIKit.UIGraphicsBeginPDFPageWithInfo
import platform.UIKit.UIGraphicsEndPDFContext
import platform.UIKit.UIImageJPEGRepresentation
import platform.UIKit.UIImagePickerController
import platform.UIKit.UIImagePickerControllerDelegateProtocol
import platform.UIKit.UIImagePickerControllerOriginalImage
import platform.UIKit.UIImagePickerControllerSourceType
import platform.UIKit.UINavigationControllerDelegateProtocol
import platform.UIKit.UIViewController
import platform.UniformTypeIdentifiers.UTTypeImage
import platform.VisionKit.VNDocumentCameraScan
import platform.VisionKit.VNDocumentCameraViewController
import platform.VisionKit.VNDocumentCameraViewControllerDelegateProtocol
import platform.UniformTypeIdentifiers.UTTypePDF
import platform.darwin.NSObject
import platform.posix.memcpy

private fun NSData.toByteArray(): ByteArray {
    val size = length.toInt()
    val bytes = ByteArray(size)
    if (size > 0) bytes.usePinned { memcpy(it.addressOf(0), this.bytes, length) }
    return bytes
}

private fun topController(): UIViewController? {
    var vc = UIApplication.sharedApplication.keyWindow?.rootViewController
    while (vc?.presentedViewController != null) vc = vc.presentedViewController
    return vc
}

private fun stamp() = NSDate().timeIntervalSince1970.toLong()

private class CameraDelegate(val onResult: (CapturedFile?) -> Unit) : NSObject(),
    UIImagePickerControllerDelegateProtocol, UINavigationControllerDelegateProtocol {
    override fun imagePickerController(picker: UIImagePickerController, didFinishPickingMediaWithInfo: Map<Any?, *>) {
        val image = didFinishPickingMediaWithInfo[UIImagePickerControllerOriginalImage] as? UIImage
        val data = image?.let { UIImageJPEGRepresentation(it, 0.85) }
        picker.dismissViewControllerAnimated(true, null)
        onResult(data?.let { CapturedFile(it.toByteArray(), "fatura-${stamp()}.jpg", "image/jpeg") })
    }

    override fun imagePickerControllerDidCancel(picker: UIImagePickerController) {
        picker.dismissViewControllerAnimated(true, null)
        onResult(null)
    }
}

/**
 * VisionKit document camera: detects the invoice edges, crops, fixes perspective and enhances the image.
 * One page → JPEG; several pages → a single PDF.
 */
private class ScannerDelegate(val onResult: (CapturedFile?) -> Unit) : NSObject(), VNDocumentCameraViewControllerDelegateProtocol {
    override fun documentCameraViewController(controller: VNDocumentCameraViewController, didFinishWithScan: VNDocumentCameraScan) {
        controller.dismissViewControllerAnimated(true, null)
        val scan = didFinishWithScan
        val count = scan.pageCount.toInt()
        if (count == 0) return onResult(null)
        if (count == 1) {
            val data = UIImageJPEGRepresentation(scan.imageOfPageAtIndex(0u), 0.85)
            onResult(data?.let { CapturedFile(it.toByteArray(), "fatura-${stamp()}.jpg", "image/jpeg") })
            return
        }
        val pdf = NSMutableData()
        UIGraphicsBeginPDFContextToData(pdf, CGRectMake(0.0, 0.0, 612.0, 792.0), null)
        for (i in 0 until count) {
            val image = scan.imageOfPageAtIndex(i.toULong())
            image.size.useContents {
                val rect = CGRectMake(0.0, 0.0, width, height)
                UIGraphicsBeginPDFPageWithInfo(rect, null)
                image.drawInRect(rect)
            }
        }
        UIGraphicsEndPDFContext()
        onResult(CapturedFile(pdf.toByteArray(), "fatura-${stamp()}.pdf", "application/pdf"))
    }

    override fun documentCameraViewControllerDidCancel(controller: VNDocumentCameraViewController) {
        controller.dismissViewControllerAnimated(true, null)
        onResult(null)
    }

    override fun documentCameraViewController(controller: VNDocumentCameraViewController, didFailWithError: NSError) {
        controller.dismissViewControllerAnimated(true, null)
        onResult(null)
    }
}

private class PickerDelegate(val onResult: (CapturedFile?) -> Unit) : NSObject(), UIDocumentPickerDelegateProtocol {
    override fun documentPicker(controller: UIDocumentPickerViewController, didPickDocumentsAtURLs: List<*>) {
        val url = didPickDocumentsAtURLs.firstOrNull() as? NSURL ?: return onResult(null)
        val access = url.startAccessingSecurityScopedResource()
        val data = NSData.dataWithContentsOfURL(url)
        if (access) url.stopAccessingSecurityScopedResource()
        val name = url.lastPathComponent ?: "documento"
        val mime = if (name.lowercase().endsWith(".pdf")) "application/pdf" else "image/jpeg"
        onResult(data?.let { CapturedFile(it.toByteArray(), name, mime) })
    }

    override fun documentPickerWasCancelled(controller: UIDocumentPickerViewController) = onResult(null)
}

@Composable
actual fun rememberDocumentCapture(onResult: (CapturedFile?) -> Unit): DocumentCapture {
    // Delegates are held strongly here: UIKit keeps only weak references.
    val camera = remember { CameraDelegate(onResult) }
    val scanner = remember { ScannerDelegate(onResult) }
    val picker = remember { PickerDelegate(onResult) }
    return remember {
        object : DocumentCapture {
            override fun takePhoto() {
                val hasCamera = UIImagePickerController.isSourceTypeAvailable(UIImagePickerControllerSourceType.UIImagePickerControllerSourceTypeCamera)
                if (hasCamera) {
                    val c = VNDocumentCameraViewController()
                    c.delegate = scanner
                    topController()?.presentViewController(c, true, null)
                } else {
                    // Simulator / no camera: pick from the photo library
                    val c = UIImagePickerController()
                    c.sourceType = UIImagePickerControllerSourceType.UIImagePickerControllerSourceTypePhotoLibrary
                    c.delegate = camera
                    topController()?.presentViewController(c, true, null)
                }
            }

            override fun pickFile() {
                val p = UIDocumentPickerViewController(forOpeningContentTypes = listOf(UTTypePDF, UTTypeImage))
                p.delegate = picker
                topController()?.presentViewController(p, true, null)
            }
        }
    }
}

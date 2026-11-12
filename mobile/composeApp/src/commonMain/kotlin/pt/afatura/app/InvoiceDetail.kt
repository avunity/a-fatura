package pt.afatura.app

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.OpenInNew
import androidx.compose.material.icons.outlined.Description
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.Warning
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private val DOC_TYPES = mapOf(
    "FT" to "Fatura", "FS" to "Fatura simplificada", "FR" to "Fatura-recibo",
    "NC" to "Nota de crédito", "ND" to "Nota de débito", "RG" to "Recibo",
)
private val SOURCES = mapOf("Upload" to "Web", "Mobile" to "App", "Email" to "Email", "Manual" to "Manual")
private val REIMB = mapOf("Pending" to "Pendente", "Approved" to "Aprovado", "Paid" to "Pago", "Rejected" to "Rejeitado", "None" to "—")

private fun docType(t: String?) = t?.let { DOC_TYPES[it.uppercase()]?.let { l -> "$l ($t)" } ?: it }

/** "2026-11-12T18:03:11.123Z" → "12/11/2026 18:03" */
private fun ptDateTime(iso: String?): String? {
    if (iso.isNullOrBlank()) return null
    val date = ptDate(iso.take(10)) ?: return iso
    val time = iso.drop(11).take(5)
    return if (time.length == 5) "$date $time" else date
}

private fun num(v: Double?, unit: String): String? = v?.let {
    val s = ((it * 100).toLong() / 100.0).toString().replace('.', ',').removeSuffix(",0")
    "$s $unit"
}

@Composable
fun InvoiceDetailScreen(api: Api, orgId: String, invoiceId: String, onBack: () -> Unit) {
    var inv by remember { mutableStateOf<InvoiceDetail?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var file by remember { mutableStateOf<CapturedFile?>(null) }
    var preview by remember { mutableStateOf<ImageBitmap?>(null) }
    var fileState by remember { mutableStateOf("idle") } // idle | loading | ready | failed
    val open = rememberDocumentOpener()
    val scope = rememberCoroutineScope()

    PlatformBackHandler(true, onBack)

    suspend fun load() {
        runCatching { api.invoice(orgId, invoiceId) }
            .onSuccess { inv = it; error = null }
            .onFailure { if (inv == null) error = it.message ?: "Erro ao carregar" }
    }

    suspend fun loadFile(d: InvoiceDetail) {
        fileState = "loading"
        runCatching { api.invoiceFile(orgId, d) }
            .onSuccess { f ->
                file = f
                preview = withContext(Dispatchers.Default) { decodePreview(f) }
                fileState = "ready"
            }
            .onFailure { fileState = "failed" }
    }

    LaunchedEffect(invoiceId) {
        load()
        inv?.let { loadFile(it) }
        // Keep the screen current while the server is still reading the document
        while (inv?.status == "Pending" || inv?.status == "Processing") { delay(5000); load() }
    }

    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(Modifier.fillMaxSize()) {
            Row(
                Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface).statusBarsPadding().padding(horizontal = 4.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Voltar") }
                Column(Modifier.weight(1f)) {
                    Text(inv?.supplierName ?: inv?.fileName ?: "Documento", fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    inv?.let { Text(listOfNotNull(docType(it.documentType), it.documentNumber).joinToString(" · ").ifEmpty { "Detalhes do documento" }, style = MaterialTheme.typography.bodySmall, color = C.muted, maxLines = 1, overflow = TextOverflow.Ellipsis) }
                }
                IconButton(onClick = { scope.launch { load() } }) { Icon(Icons.Outlined.Refresh, "Atualizar", tint = C.muted) }
            }
            HorizontalDivider(color = C.line)

            val d = inv
            when {
                d == null && error != null -> Column(Modifier.fillMaxSize().padding(32.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(error!!, color = C.bad, textAlign = TextAlign.Center)
                    Spacer(Modifier.height(12.dp))
                    OutlinedButton(onClick = { scope.launch { error = null; load(); inv?.let { loadFile(it) } } }) { Text("Tentar novamente") }
                }
                d == null -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator(strokeWidth = 3.dp, modifier = Modifier.size(28.dp)) }
                else -> DetailBody(d, preview, fileState, onOpenFile = { file?.let(open) }, onRetryFile = { scope.launch { loadFile(d) } })
            }
        }
    }
}

@Composable
private fun DetailBody(d: InvoiceDetail, preview: ImageBitmap?, fileState: String, onOpenFile: () -> Unit, onRetryFile: () -> Unit) {
    val st = statusStyle(d.status, d.flags.isNotEmpty())
    LazyColumn(Modifier.fillMaxSize().navigationBarsPadding(), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        // Resumo: total + estado
        item {
            SectionCard {
                Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Total", style = MaterialTheme.typography.bodySmall, color = C.muted)
                        Text(eur(d.totalAmount), fontSize = 26.sp, fontWeight = FontWeight.Bold)
                        Text(listOfNotNull(ptDate(d.issueDate), d.vatAmount?.let { "IVA ${eur(it)}" }).joinToString(" · "), style = MaterialTheme.typography.bodySmall, color = C.muted)
                    }
                    Column(horizontalAlignment = Alignment.End) {
                        Row(Modifier.clip(CircleShape).background(st.bg).padding(horizontal = 10.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                            Icon(st.icon, null, tint = st.fg, modifier = Modifier.size(14.dp))
                            Spacer(Modifier.width(4.dp))
                            Text(st.label, color = st.fg, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                        }
                        Spacer(Modifier.height(6.dp))
                        Text(if (d.collectedInEFatura) "e-Fatura ✓ recolhida" else "e-Fatura: por confirmar", fontSize = 12.sp,
                            color = if (d.collectedInEFatura) C.ok else C.muted)
                    }
                }
                if (d.status == "Pending" || d.status == "Processing") {
                    Text("A ler o documento… esta página atualiza sozinha.", style = MaterialTheme.typography.bodySmall, color = C.info, modifier = Modifier.padding(bottom = 8.dp))
                }
            }
        }

        // Avisos
        val warnings = listOfNotNull(d.processingError) + d.flags + (if (d.duplicateOfId != null) listOf("Possível duplicado de outro documento") else emptyList())
        if (warnings.isNotEmpty()) item {
            Column(Modifier.fillMaxWidth().clip(MaterialTheme.shapes.medium).background(C.warnSoft).padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                warnings.forEach { w ->
                    Row(verticalAlignment = Alignment.Top) {
                        Icon(Icons.Outlined.Warning, null, tint = C.warn, modifier = Modifier.size(16.dp).padding(top = 2.dp))
                        Spacer(Modifier.width(8.dp))
                        Text(w, color = C.warn, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
        }

        // Documento original
        item {
            SectionCard {
                SectionTitle("Documento original")
                when (fileState) {
                    "loading" -> Box(Modifier.fillMaxWidth().height(160.dp), contentAlignment = Alignment.Center) { CircularProgressIndicator(strokeWidth = 3.dp, modifier = Modifier.size(24.dp)) }
                    "failed" -> Row(Modifier.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text("Não foi possível obter o ficheiro.", color = C.muted, modifier = Modifier.weight(1f))
                        OutlinedButton(onClick = onRetryFile) { Text("Tentar") }
                    }
                    "ready" -> {
                        if (preview != null) {
                            Image(
                                preview, "Documento", contentScale = ContentScale.Fit,
                                modifier = Modifier.fillMaxWidth().heightIn(max = 460.dp).clip(RoundedCornerShape(10.dp))
                                    .background(C.line).clickable(onClick = onOpenFile),
                            )
                            Spacer(Modifier.height(8.dp))
                        } else {
                            Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                                Icon(Icons.Outlined.Description, null, tint = C.muted, modifier = Modifier.size(32.dp))
                                Spacer(Modifier.width(10.dp))
                                Text(d.fileName ?: "documento", maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                            }
                        }
                        OutlinedButton(onClick = onOpenFile, modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp), border = BorderStroke(1.dp, C.line)) {
                            Icon(Icons.AutoMirrored.Outlined.OpenInNew, null, modifier = Modifier.size(16.dp))
                            Spacer(Modifier.width(6.dp))
                            Text(if (d.fileContentType == "application/pdf" || d.fileName?.lowercase()?.endsWith(".pdf") == true) "Abrir PDF" else "Abrir imagem")
                        }
                    }
                    else -> Spacer(Modifier.height(8.dp))
                }
            }
        }

        item {
            SectionCard {
                SectionTitle("Dados fiscais")
                Field("Emitente", d.supplierName)
                Field("NIF emitente", d.supplierNif)
                Field("NIF adquirente", d.buyerNif)
                Field("Tipo", docType(d.documentType))
                Field("Número", d.documentNumber)
                Field("ATCUD", d.atcud)
                Field("Data", ptDate(d.issueDate))
                Field("Código QR AT", if (d.hasValidQr) "lido ✓" else "não encontrado")
            }
        }

        item {
            SectionCard {
                SectionTitle("Valores")
                Field("Base tributável", d.netAmount?.let(::eur))
                Field("IVA", d.vatAmount?.let(::eur))
                Field("Total", d.totalAmount?.let(::eur), bold = true)
                if (d.vatLines.isNotEmpty()) {
                    Spacer(Modifier.height(6.dp))
                    Row(Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
                        listOf("Taxa", "Base", "IVA").forEachIndexed { i, h ->
                            Text(h, style = MaterialTheme.typography.bodySmall, color = C.muted, modifier = Modifier.weight(1f), textAlign = if (i == 0) TextAlign.Start else TextAlign.End)
                        }
                    }
                    d.vatLines.forEach { l ->
                        Row(Modifier.fillMaxWidth().padding(vertical = 3.dp)) {
                            val rate = (if (l.rate % 1.0 == 0.0) l.rate.toLong().toString() else l.rate.toString().replace('.', ',')) + "%" +
                                (l.region?.takeIf { it != "PT" }?.let { " ($it)" } ?: "")
                            Text(rate, modifier = Modifier.weight(1f))
                            Text(eur(l.base), modifier = Modifier.weight(1f), textAlign = TextAlign.End)
                            Text(eur(l.vat), modifier = Modifier.weight(1f), textAlign = TextAlign.End)
                        }
                    }
                    Spacer(Modifier.height(6.dp))
                }
            }
        }

        if (d.subDocuments.size > 1) item {
            SectionCard {
                SectionTitle("Faturas incluídas neste documento (${d.subDocuments.size})")
                d.subDocuments.forEachIndexed { i, s ->
                    if (i > 0) HorizontalDivider(color = C.line)
                    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(listOfNotNull(s.documentType, s.documentNumber).joinToString(" ").ifEmpty { "—" }, fontWeight = FontWeight.Medium)
                            Text(listOfNotNull(ptDate(s.issueDate), s.atcud).joinToString(" · "), style = MaterialTheme.typography.bodySmall, color = C.muted)
                        }
                        Text(eur(s.total), fontWeight = FontWeight.SemiBold)
                    }
                }
            }
        }

        item {
            SectionCard {
                SectionTitle("Classificação")
                Field("Categoria", d.categoryName ?: "—")
                Field("Projeto", d.projectName)
                Field("Viatura", d.vehiclePlate)
                Field("Matrícula detetada", d.licensePlateDetected?.takeIf { it != d.vehiclePlate })
                Field("Combustível", num(d.fuelLiters, "L"))
                Field("Energia", num(d.energyKwh, "kWh"))
                Field("Etiquetas", d.tags.takeIf { it.isNotEmpty() }?.joinToString(", "))
                d.classificationReason?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = C.muted, modifier = Modifier.padding(vertical = 6.dp)) }
            }
        }

        if (d.paidPersonally || !d.notes.isNullOrBlank() || !d.summary.isNullOrBlank()) item {
            SectionCard {
                SectionTitle("Notas")
                if (d.paidPersonally) Field("Pago do próprio bolso", "Sim · reembolso ${(REIMB[d.reimbursement] ?: d.reimbursement).lowercase()}")
                d.summary?.takeIf { it.isNotBlank() }?.let { Field("Resumo", it) }
                d.notes?.takeIf { it.isNotBlank() }?.let { Field("Notas", it) }
            }
        }

        item {
            SectionCard {
                SectionTitle("Registo")
                Field("Carregada por", d.uploadedByName.ifBlank { null })
                Field("Origem", SOURCES[d.source] ?: d.source)
                Field("Carregada em", ptDateTime(d.createdAt))
                Field("Processada em", ptDateTime(d.processedAt))
                Field("Revista em", ptDateTime(d.reviewedAt))
                Field("Ficheiro", d.fileName)
            }
        }
        item { Spacer(Modifier.height(12.dp)) }
    }
}

@Composable
private fun SectionTitle(text: String) =
    Text(text, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold, modifier = Modifier.padding(top = 6.dp, bottom = 4.dp))

/** Label/value row; skipped when there is no value. */
@Composable
private fun Field(label: String, value: String?, bold: Boolean = false) {
    if (value.isNullOrBlank()) return
    Row(Modifier.fillMaxWidth().padding(vertical = 5.dp), verticalAlignment = Alignment.Top) {
        Text(label, color = C.muted, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.width(140.dp))
        Text(value, style = MaterialTheme.typography.bodyMedium, fontWeight = if (bold) FontWeight.Bold else FontWeight.Normal, modifier = Modifier.weight(1f), textAlign = TextAlign.End)
    }
}

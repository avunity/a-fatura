package pt.afatura.app

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.BorderStroke
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
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.AccountCircle
import androidx.compose.material.icons.outlined.CameraAlt
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.KeyboardArrowUp
import androidx.compose.material.icons.outlined.PictureAsPdf
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.DirectionsCar
import androidx.compose.material.icons.outlined.DocumentScanner
import androidx.compose.material.icons.outlined.ErrorOutline
import androidx.compose.material.icons.outlined.Folder
import androidx.compose.material.icons.outlined.HourglassTop
import androidx.compose.material.icons.outlined.Logout
import androidx.compose.material.icons.outlined.Mail
import androidx.compose.material.icons.outlined.Receipt
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.UnfoldMore
import androidx.compose.material.icons.outlined.UploadFile
import androidx.compose.material.icons.outlined.Visibility
import androidx.compose.material.icons.outlined.VisibilityOff
import androidx.compose.material.icons.outlined.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@Composable
fun App(api: Api = remember { Api() }) {
    var themeMode by remember { mutableStateOf(api.theme) }
    AFaturaTheme(themeMode) {
        var me by remember { mutableStateOf<Me?>(null) }
        var checking by remember { mutableStateOf(api.token != null) }

        LaunchedEffect(Unit) {
            if (api.token != null) me = runCatching { api.me() }.getOrNull()
            checking = false
        }

        Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
            when {
                checking -> Splash()
                me == null -> LoginScreen(api) { me = it }
                else -> MainScreen(api, me!!, themeMode, { api.theme = it; themeMode = it }) { api.token = null; me = null }
            }
        }
    }
}

@Composable
private fun Splash() = Column(
    Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally,
) {
    Logo(72.dp)
    Spacer(Modifier.height(24.dp))
    CircularProgressIndicator(strokeWidth = 3.dp, modifier = Modifier.size(28.dp))
}

// ───────────────────────── Login ─────────────────────────

@Composable
private fun LoginScreen(api: Api, onLogged: (Me) -> Unit) {
    var server by remember { mutableStateOf(api.baseUrl) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var showPassword by remember { mutableStateOf(false) }
    var showServer by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Column(Modifier.fillMaxSize()) {
        // Gradient header
        Box(
            Modifier.fillMaxWidth().height(260.dp).background(Brand.Gradient).padding(horizontal = 28.dp),
            contentAlignment = Alignment.CenterStart,
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Box(Modifier.size(64.dp).clip(RoundedCornerShape(18.dp)).background(Color.White.copy(alpha = 0.18f)), contentAlignment = Alignment.Center) {
                    Text("A€", color = Color.White, fontWeight = FontWeight.ExtraBold, fontSize = 24.sp)
                }
                Text("A-Fatura", color = Color.White, fontWeight = FontWeight.ExtraBold, fontSize = 32.sp)
                Text("Fotografe as faturas. Nós tratamos do resto.", color = Color.White.copy(alpha = 0.85f), fontSize = 15.sp)
            }
        }

        Card(
            Modifier.fillMaxWidth().padding(horizontal = 20.dp).offset(y = (-28).dp),
            shape = MaterialTheme.shapes.large,
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
            elevation = CardDefaults.cardElevation(defaultElevation = 6.dp),
        ) {
            Column(Modifier.padding(22.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Text("Entrar", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                OutlinedTextField(
                    email, { email = it }, label = { Text("Email") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    shape = MaterialTheme.shapes.small, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                )
                OutlinedTextField(
                    password, { password = it }, label = { Text("Palavra-passe") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
                    shape = MaterialTheme.shapes.small,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                    trailingIcon = {
                        IconButton(onClick = { showPassword = !showPassword }) {
                            Icon(if (showPassword) Icons.Outlined.VisibilityOff else Icons.Outlined.Visibility, "Mostrar palavra-passe")
                        }
                    },
                )
                AnimatedVisibility(showServer) {
                    OutlinedTextField(server, { server = it }, label = { Text("Servidor") }, singleLine = true,
                        modifier = Modifier.fillMaxWidth(), shape = MaterialTheme.shapes.small)
                }
                error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
                Button(
                    enabled = !busy && email.isNotBlank() && password.isNotBlank(),
                    modifier = Modifier.fillMaxWidth().height(52.dp), shape = MaterialTheme.shapes.small,
                    onClick = {
                        busy = true; error = null
                        scope.launch {
                            try {
                                api.baseUrl = server
                                api.login(email.trim(), password)
                                onLogged(api.me())
                            } catch (e: Exception) { error = e.message ?: "Erro ao entrar" } finally { busy = false }
                        }
                    },
                ) {
                    if (busy) CircularProgressIndicator(Modifier.size(20.dp), color = Color.White, strokeWidth = 2.dp)
                    else Text("Entrar", fontWeight = FontWeight.SemiBold)
                }
                TextButton(onClick = { showServer = !showServer }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                    Text(if (showServer) "Ocultar servidor" else "Servidor: ${server.removePrefix("https://")}", style = MaterialTheme.typography.bodySmall)
                }
            }
        }
    }
}

// ───────────────────────── Main ─────────────────────────

private enum class Tab(val label: String, val icon: ImageVector) {
    Send("Enviar", Icons.Outlined.CameraAlt),
    Docs("Documentos", Icons.Outlined.Receipt),
    Account("Conta", Icons.Outlined.AccountCircle),
}

private data class Pending(val id: Long, val files: List<CapturedFile>, val failed: String? = null) {
    val label get() = if (files.size == 1) files[0].fileName else "Fatura com ${files.size} partes"
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun MainScreen(api: Api, me: Me, themeMode: String, onTheme: (String) -> Unit, onLogout: () -> Unit) {
    val orgs = remember(me) {
        me.memberships.map { Triple(it.organizationId, it.name, if (it.type == "Individual") "Pessoal" else if (it.role == "Manager") "Gestor" else "Colaborador") } +
            me.clients.filter { c -> me.memberships.none { it.organizationId == c.organizationId } }.map { Triple(it.organizationId, it.name, "Cliente") }
    }
    var orgId by remember { mutableStateOf(api.orgId?.takeIf { id -> orgs.any { it.first == id } } ?: orgs.firstOrNull()?.first) }
    var tab by remember { mutableStateOf(Tab.Send) }
    var vehicles by remember { mutableStateOf<List<Vehicle>>(emptyList()) }
    var projects by remember { mutableStateOf<List<Project>>(emptyList()) }
    var vehicleId by remember { mutableStateOf<String?>(null) }
    var projectId by remember { mutableStateOf<String?>(null) }
    var paidPersonally by remember { mutableStateOf(false) }
    var recent by remember { mutableStateOf<List<InvoiceItem>>(emptyList()) }
    var refreshing by remember { mutableStateOf(false) }
    var orgSheet by remember { mutableStateOf(false) }
    var openId by remember { mutableStateOf<String?>(null) }
    val queue = remember { mutableStateListOf<Pending>() }
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

    suspend fun refresh() {
        val org = orgId ?: return
        recent = runCatching { api.recentInvoices(org) }.getOrDefault(recent)
    }

    LaunchedEffect(orgId) {
        val org = orgId ?: return@LaunchedEffect
        api.orgId = org
        vehicleId = null; projectId = null
        vehicles = runCatching { api.vehicles(org).filter { it.isActive } }.getOrDefault(emptyList())
        projects = runCatching { api.projects(org).filter { it.isActive } }.getOrDefault(emptyList())
        while (true) { refresh(); delay(8000) } // statuses change while the server processes documents
    }

    // Cada captura fica aqui até "Enviar": permite acrescentar mais páginas à mesma fatura antes de enviar
    val draft = remember { mutableStateListOf<CapturedFile>() }

    fun send(files: List<CapturedFile>) {
        val org = orgId ?: return
        if (files.isEmpty()) return
        val item = Pending(kotlin.random.Random.nextLong(), files)
        queue.add(0, item)
        scope.launch {
            try {
                api.upload(org, files, vehicleId, paidPersonally, null, projectId)
                queue.removeAll { it.id == item.id }
                refresh()
                snackbar.showSnackbar("Enviado — a processar ${item.label}")
            } catch (e: Exception) {
                val idx = queue.indexOfFirst { it.id == item.id }
                if (idx >= 0) queue[idx] = item.copy(failed = e.message ?: "erro")
            }
        }
    }

    val capture = rememberDocumentCapture { file ->
        if (file != null) draft.add(file)
    }
    val current = orgs.firstOrNull { it.first == orgId }

    val detailOrg = orgId
    if (openId != null && detailOrg != null) {
        InvoiceDetailScreen(api, detailOrg, openId!!) { openId = null; scope.launch { refresh() } }
        return
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            NavigationBar(containerColor = MaterialTheme.colorScheme.surface, tonalElevation = 0.dp) {
                Tab.entries.forEach { t ->
                    NavigationBarItem(
                        selected = tab == t, onClick = { tab = t },
                        icon = { Icon(t.icon, t.label) }, label = { Text(t.label) },
                    )
                }
            }
        },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            // Header with organization switcher
            Row(
                Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surface).padding(horizontal = 16.dp, vertical = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Logo(36.dp)
                Spacer(Modifier.width(12.dp))
                Row(
                    Modifier.weight(1f).clip(MaterialTheme.shapes.small).clickable(enabled = orgs.size > 1) { orgSheet = true }.padding(6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(current?.second ?: "A-Fatura", fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(current?.third ?: "", style = MaterialTheme.typography.bodySmall, color = C.muted)
                    }
                    if (orgs.size > 1) Icon(Icons.Outlined.UnfoldMore, "Trocar entidade", tint = C.muted)
                }
            }
            HorizontalDivider(color = C.line)

            when (tab) {
                Tab.Send -> SendTab(
                    me = me, orgId = orgId, capture = capture, queue = queue,
                    vehicles = vehicles, vehicleId = vehicleId, onVehicle = { vehicleId = it },
                    projects = projects, projectId = projectId, onProject = { projectId = it },
                    paidPersonally = paidPersonally, onPaid = { paidPersonally = it },
                    recent = recent.take(3), onRetry = { p -> queue.removeAll { it.id == p.id }; send(p.files) },
                    draft = draft, onSendDraft = { send(draft.toList()); draft.clear() },
                    onSeeAll = { tab = Tab.Docs }, onOpen = { openId = it },
                )
                Tab.Docs -> PullToRefreshBox(
                    isRefreshing = refreshing,
                    onRefresh = { scope.launch { refreshing = true; refresh(); refreshing = false } },
                    modifier = Modifier.fillMaxSize(),
                ) { DocsTab(recent) { openId = it } }
                Tab.Account -> AccountTab(api, me, themeMode, onTheme, onLogout)
            }
        }
    }

    if (orgSheet) {
        ModalBottomSheet(onDismissRequest = { orgSheet = false }, containerColor = MaterialTheme.colorScheme.surface) {
            Text("Escolher entidade", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp))
            orgs.forEach { (id, name, tag) ->
                Row(
                    Modifier.fillMaxWidth().clickable { orgId = id; orgSheet = false }.padding(horizontal = 20.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Avatar(name, 40.dp)
                    Spacer(Modifier.width(14.dp))
                    Column(Modifier.weight(1f)) {
                        Text(name, fontWeight = FontWeight.Medium)
                        Text(tag, style = MaterialTheme.typography.bodySmall, color = C.muted)
                    }
                    if (id == orgId) Icon(Icons.Outlined.CheckCircle, null, tint = MaterialTheme.colorScheme.primary)
                }
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}

// ───────────────────────── Enviar ─────────────────────────

@Composable
private fun SendTab(
    me: Me, orgId: String?, capture: DocumentCapture, queue: List<Pending>,
    vehicles: List<Vehicle>, vehicleId: String?, onVehicle: (String?) -> Unit,
    projects: List<Project>, projectId: String?, onProject: (String?) -> Unit,
    paidPersonally: Boolean, onPaid: (Boolean) -> Unit,
    recent: List<InvoiceItem>, onRetry: (Pending) -> Unit, onSeeAll: () -> Unit, onOpen: (String) -> Unit,
    draft: MutableList<CapturedFile>, onSendDraft: () -> Unit,
) {
    val clipboard = LocalClipboardManager.current
    val mailbox = me.memberships.firstOrNull { it.organizationId == orgId }?.mailbox

    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        item {
            // Hero capture card
            Box(
                Modifier.fillMaxWidth().clip(MaterialTheme.shapes.large).background(Brand.Gradient)
                    .clickable { capture.takePhoto() }.padding(22.dp),
            ) {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    Box(Modifier.size(56.dp).clip(CircleShape).background(Color.White.copy(alpha = 0.2f)), contentAlignment = Alignment.Center) {
                        Icon(Icons.Outlined.DocumentScanner, null, tint = Color.White, modifier = Modifier.size(30.dp))
                    }
                    Column {
                        Text(if (draft.isNotEmpty()) "Adicionar página" else "Digitalizar fatura", color = Color.White, fontWeight = FontWeight.Bold, fontSize = 22.sp)
                        Text(
                            if (draft.isNotEmpty()) "Digitalize a parte seguinte da mesma fatura"
                            else "Deteta a fatura, corta o fundo e melhora a imagem. Pode digitalizar várias páginas seguidas.",
                            color = Color.White.copy(alpha = 0.85f), fontSize = 14.sp,
                        )
                    }
                }
            }
        }
        item {
            OutlinedButton(
                onClick = { capture.pickFile() }, modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = MaterialTheme.shapes.medium, border = BorderStroke(1.dp, C.line),
                colors = ButtonDefaults.outlinedButtonColors(containerColor = MaterialTheme.colorScheme.surface),
            ) {
                Icon(Icons.Outlined.UploadFile, null)
                Spacer(Modifier.width(8.dp))
                Text(if (draft.isNotEmpty()) "Adicionar PDF ou imagem" else "Escolher PDF ou imagem")
            }
        }

        if (draft.isNotEmpty()) item { DraftCard(draft, onSendDraft, onAddPage = { capture.takePhoto() }) }

        item {
            SectionCard {
                if (projects.isNotEmpty())
                    PickerRow(Icons.Outlined.Folder, "Projeto", projects.firstOrNull { it.id == projectId }?.name ?: "Sem projeto",
                        listOf(null to "Sem projeto") + projects.map { it.id to it.name }, onProject)
                if (projects.isNotEmpty() && vehicles.isNotEmpty()) HorizontalDivider(color = C.line)
                if (vehicles.isNotEmpty())
                    PickerRow(Icons.Outlined.DirectionsCar, "Viatura", vehicles.firstOrNull { it.id == vehicleId }?.plate ?: "Detetar automaticamente",
                        listOf(null to "Detetar automaticamente") + vehicles.map { it.id to it.plate }, onVehicle)
                if (projects.isNotEmpty() || vehicles.isNotEmpty()) HorizontalDivider(color = C.line)
                Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text("Paguei do meu bolso", fontWeight = FontWeight.Medium)
                        Text("Pedir reembolso à empresa", style = MaterialTheme.typography.bodySmall, color = C.muted)
                    }
                    Switch(paidPersonally, onPaid)
                }
            }
        }

        items(queue, key = { it.id }) { p ->
            Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), shape = MaterialTheme.shapes.medium,
                modifier = Modifier.fillMaxWidth().clickable(enabled = p.failed != null) { onRetry(p) }) {
                Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(if (p.failed != null) Icons.Outlined.ErrorOutline else Icons.Outlined.UploadFile, null,
                            tint = if (p.failed != null) C.bad else C.info)
                        Spacer(Modifier.width(10.dp))
                        Text(p.label, Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Medium)
                        if (p.failed != null) Icon(Icons.Outlined.Refresh, "Repetir", tint = C.muted)
                    }
                    if (p.failed == null) LinearProgressIndicator(Modifier.fillMaxWidth().clip(CircleShape))
                    else Text("Falhou: ${p.failed} — toque para repetir", style = MaterialTheme.typography.bodySmall, color = C.bad)
                }
            }
        }

        if (recent.isNotEmpty()) {
            item {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Últimos documentos", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                    TextButton(onClick = onSeeAll) { Text("Ver todos") }
                }
            }
            items(recent, key = { "r" + it.id }) { InvoiceRow(it, onOpen) }
        }

        mailbox?.let { addr ->
            item {
                SectionCard {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Outlined.Mail, null, tint = MaterialTheme.colorScheme.primary)
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f)) {
                            Text("Reencaminhe faturas por email", fontWeight = FontWeight.Medium)
                            Text(addr, style = MaterialTheme.typography.bodySmall, color = C.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        IconButton(onClick = { clipboard.setText(AnnotatedString(addr)) }) { Icon(Icons.Outlined.ContentCopy, "Copiar") }
                    }
                }
            }
        }
    }
}

@Composable
private fun DraftCard(draft: MutableList<CapturedFile>, onSend: () -> Unit, onAddPage: () -> Unit) {
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), shape = MaterialTheme.shapes.medium,
        border = BorderStroke(1.5.dp, MaterialTheme.colorScheme.primary), modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(if (draft.size == 1) "Pronta a enviar" else "Pronta a enviar · ${draft.size} partes", fontWeight = FontWeight.Bold)
            Text(
                if (draft.size == 1) "Se a fatura não coube numa só imagem, adicione as páginas seguintes antes de enviar."
                else "Vão ser juntas num único documento, por esta ordem.",
                style = MaterialTheme.typography.bodySmall, color = C.muted,
            )
            draft.forEachIndexed { i, f ->
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Icon(if (f.mimeType == "application/pdf") Icons.Outlined.PictureAsPdf else Icons.Outlined.Image, null, tint = C.muted, modifier = Modifier.size(20.dp))
                    Spacer(Modifier.width(8.dp))
                    Column(Modifier.weight(1f)) {
                        Text("${i + 1}. ${if (f.mimeType == "application/pdf") "PDF" else "Foto"}", fontWeight = FontWeight.Medium, fontSize = 14.sp)
                        Text("${f.fileName} · ${(f.bytes.size / 1024).coerceAtLeast(1)} KB", style = MaterialTheme.typography.bodySmall, color = C.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    if (i > 0) IconButton(onClick = { val x = draft.removeAt(i); draft.add(i - 1, x) }) { Icon(Icons.Outlined.KeyboardArrowUp, "Subir", tint = C.muted) }
                    IconButton(onClick = { draft.removeAt(i) }) { Icon(Icons.Outlined.Delete, "Remover", tint = C.bad) }
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.padding(top = 4.dp)) {
                IconButton(onClick = { draft.clear() }) { Icon(Icons.Outlined.Delete, "Descartar", tint = C.muted) }
                OutlinedButton(onClick = onAddPage, border = BorderStroke(1.dp, C.line), modifier = Modifier.weight(1f)) { Text("+ Página") }
                Button(onClick = onSend, modifier = Modifier.weight(1.3f)) { Text("Enviar") }
            }
        }
    }
}

@Composable
internal fun SectionCard(content: @Composable () -> Unit) {
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), shape = MaterialTheme.shapes.medium,
        border = BorderStroke(1.dp, C.line), modifier = Modifier.fillMaxWidth(),
    ) { Column(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) { content() } }
}

@Composable
private fun PickerRow(icon: ImageVector, label: String, value: String, options: List<Pair<String?, String>>, onPick: (String?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        Row(
            Modifier.fillMaxWidth().clickable { open = true }.padding(vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(icon, null, tint = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(label, style = MaterialTheme.typography.bodySmall, color = C.muted)
                Text(value, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Icon(Icons.Outlined.UnfoldMore, null, tint = C.muted)
        }
        DropdownMenu(open, { open = false }) {
            options.forEach { (id, text) -> DropdownMenuItem(text = { Text(text) }, onClick = { onPick(id); open = false }) }
        }
    }
}

// ───────────────────────── Documentos ─────────────────────────

internal data class StatusStyle(val label: String, val fg: Color, val bg: Color, val icon: ImageVector)

@Composable
internal fun statusStyle(s: String, flagged: Boolean): StatusStyle = when (s) {
    "Processed" -> StatusStyle("Processada", C.ok, C.okSoft, Icons.Outlined.CheckCircle)
    "NeedsReview" -> StatusStyle("Rever", C.warn, C.warnSoft, Icons.Outlined.Warning)
    "Failed" -> StatusStyle("Erro", C.bad, C.badSoft, Icons.Outlined.ErrorOutline)
    "Processing" -> StatusStyle("A processar", C.info, C.infoSoft, Icons.Outlined.HourglassTop)
    else -> StatusStyle(if (flagged) "Rever" else "Em fila", C.info, C.infoSoft, Icons.Outlined.HourglassTop)
}

@Composable
private fun DocsTab(recent: List<InvoiceItem>, onOpen: (String) -> Unit) {
    var filter by remember { mutableStateOf("all") }
    val shown = when (filter) {
        "review" -> recent.filter { it.status == "NeedsReview" || it.status == "Failed" }
        "processing" -> recent.filter { it.status == "Pending" || it.status == "Processing" }
        else -> recent
    }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("all" to "Todos", "review" to "Para rever", "processing" to "A processar").forEach { (k, l) ->
                    FilterChip(selected = filter == k, onClick = { filter = k }, label = { Text(l) })
                }
            }
        }
        if (shown.isEmpty()) item {
            Column(Modifier.fillMaxWidth().padding(top = 60.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Icon(Icons.Outlined.Receipt, null, tint = C.line, modifier = Modifier.size(64.dp))
                Spacer(Modifier.height(12.dp))
                Text("Sem documentos", color = C.muted)
            }
        }
        items(shown, key = { it.id }) { InvoiceRow(it, onOpen) }
        item { Text("Puxe para baixo para atualizar", style = MaterialTheme.typography.bodySmall, color = C.muted, modifier = Modifier.fillMaxWidth().padding(8.dp)) }
    }
}

@Composable
private fun InvoiceRow(inv: InvoiceItem, onOpen: (String) -> Unit) {
    val st = statusStyle(inv.status, inv.flags.isNotEmpty())
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface), shape = MaterialTheme.shapes.medium,
        border = BorderStroke(1.dp, C.line), modifier = Modifier.fillMaxWidth().clip(MaterialTheme.shapes.medium).clickable { onOpen(inv.id) }) {
        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(42.dp).clip(RoundedCornerShape(12.dp)).background(st.bg), contentAlignment = Alignment.Center) {
                Icon(st.icon, null, tint = st.fg, modifier = Modifier.size(22.dp))
            }
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(inv.supplierName ?: inv.fileName ?: "Documento", fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                val meta = listOfNotNull(ptDate(inv.issueDate), inv.categoryName, inv.vehiclePlate).joinToString(" · ")
                Text(meta.ifEmpty { "a ler o documento…" }, style = MaterialTheme.typography.bodySmall, color = C.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Spacer(Modifier.width(8.dp))
            Column(horizontalAlignment = Alignment.End) {
                Text(eur(inv.totalAmount), fontWeight = FontWeight.Bold)
                Spacer(Modifier.height(4.dp))
                Box(Modifier.clip(CircleShape).background(st.bg).padding(horizontal = 8.dp, vertical = 2.dp)) {
                    Text(st.label + if (inv.collectedInEFatura) " · AT ✓" else "", color = st.fg, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

// ───────────────────────── Conta ─────────────────────────

@Composable
private fun AccountTab(api: Api, me: Me, themeMode: String, onTheme: (String) -> Unit, onLogout: () -> Unit) {
    val roleLabel = when (me.role) { "Accountant" -> "Contabilista"; "Admin" -> "Backoffice"; else -> "Utilizador" }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
        item {
            SectionCard {
                Row(Modifier.padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                    Avatar(me.name.ifBlank { me.email }, 56.dp, bg = C.userBg, fg = C.userFg)
                    Spacer(Modifier.width(14.dp))
                    Column {
                        Text(me.name, fontWeight = FontWeight.Bold, fontSize = 18.sp)
                        Text(me.email, color = C.muted, style = MaterialTheme.typography.bodySmall)
                        Text(roleLabel, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.Medium)
                    }
                }
            }
        }
        if (me.memberships.isNotEmpty()) item {
            SectionCard {
                Text("Entidades", style = MaterialTheme.typography.bodySmall, color = C.muted, modifier = Modifier.padding(top = 6.dp))
                me.memberships.forEach { m ->
                    Row(Modifier.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Avatar(m.name, 32.dp)
                        Spacer(Modifier.width(12.dp))
                        Column {
                            Text(m.name, fontWeight = FontWeight.Medium)
                            Text(if (m.type == "Individual") "Pessoal" else if (m.role == "Manager") "Gestor" else "Colaborador",
                                style = MaterialTheme.typography.bodySmall, color = C.muted)
                        }
                    }
                }
            }
        }
        item {
            SectionCard {
                Text("Aparência", style = MaterialTheme.typography.bodySmall, color = C.muted, modifier = Modifier.padding(top = 6.dp))
                Row(Modifier.padding(vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf("light" to "Claro", "dark" to "Escuro", "system" to "Sistema").forEach { (k, l) ->
                        FilterChip(selected = themeMode == k, onClick = { onTheme(k) }, label = { Text(l) })
                    }
                }
            }
        }
        item {
            SectionCard {
                Text("Servidor", style = MaterialTheme.typography.bodySmall, color = C.muted, modifier = Modifier.padding(top = 6.dp))
                Text(api.baseUrl, modifier = Modifier.padding(bottom = 8.dp))
            }
        }
        item {
            OutlinedButton(
                onClick = onLogout, modifier = Modifier.fillMaxWidth().height(52.dp), shape = MaterialTheme.shapes.medium,
                border = BorderStroke(1.dp, C.badSoft),
                colors = ButtonDefaults.outlinedButtonColors(containerColor = MaterialTheme.colorScheme.surface, contentColor = C.bad),
            ) {
                Icon(Icons.Outlined.Logout, null)
                Spacer(Modifier.width(8.dp))
                Text("Terminar sessão")
            }
        }
    }
}

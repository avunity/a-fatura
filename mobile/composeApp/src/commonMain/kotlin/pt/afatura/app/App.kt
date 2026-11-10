package pt.afatura.app

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.Logout
import androidx.compose.material.icons.filled.UploadFile
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.lightColorScheme
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private val Teal = Color(0xFF0F766E)

@Composable
fun App(api: Api = remember { Api() }) {
    MaterialTheme(colorScheme = lightColorScheme(primary = Teal)) {
        var me by remember { mutableStateOf<Me?>(null) }
        var checking by remember { mutableStateOf(api.token != null) }

        LaunchedEffect(Unit) {
            if (api.token != null) me = runCatching { api.me() }.getOrNull()
            checking = false
        }

        when {
            checking -> Centered("A carregar…")
            me == null -> LoginScreen(api) { me = it }
            else -> CaptureScreen(api, me!!) { api.token = null; me = null }
        }
    }
}

@Composable
private fun Centered(text: String) = Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) { Text(text) }

@Composable
private fun LoginScreen(api: Api, onLogged: (Me) -> Unit) {
    var server by remember { mutableStateOf(api.baseUrl) }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp, Alignment.CenterVertically)) {
        Text("A€-Fatura", style = MaterialTheme.typography.headlineLarge, color = Teal)
        Text("Fotografe as faturas e envie-as para a sua conta.", style = MaterialTheme.typography.bodyMedium)
        OutlinedTextField(email, { email = it }, label = { Text("Email") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email))
        OutlinedTextField(password, { password = it }, label = { Text("Palavra-passe") }, singleLine = true, modifier = Modifier.fillMaxWidth(),
            visualTransformation = PasswordVisualTransformation())
        OutlinedTextField(server, { server = it }, label = { Text("Servidor") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        Button(enabled = !busy, modifier = Modifier.fillMaxWidth(), onClick = {
            busy = true; error = null
            scope.launch {
                try {
                    api.baseUrl = server
                    api.login(email.trim(), password)
                    onLogged(api.me())
                } catch (e: Exception) { error = e.message ?: "Erro" } finally { busy = false }
            }
        }) { Text(if (busy) "A entrar…" else "Entrar") }
    }
}

private data class Pending(val file: CapturedFile, var state: String)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CaptureScreen(api: Api, me: Me, onLogout: () -> Unit) {
    val orgs = remember(me) { me.memberships.map { it.organizationId to it.name } + me.clients.map { it.organizationId to it.name } }
    var orgId by remember { mutableStateOf(api.orgId?.takeIf { id -> orgs.any { it.first == id } } ?: orgs.firstOrNull()?.first) }
    var vehicles by remember { mutableStateOf<List<Vehicle>>(emptyList()) }
    var vehicleId by remember { mutableStateOf<String?>(null) }
    var projects by remember { mutableStateOf<List<Project>>(emptyList()) }
    var projectId by remember { mutableStateOf<String?>(null) }
    var projectMenu by remember { mutableStateOf(false) }
    var paidPersonally by remember { mutableStateOf(false) }
    var recent by remember { mutableStateOf<List<InvoiceItem>>(emptyList()) }
    val queue = remember { mutableStateListOf<Pending>() }
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    var orgMenu by remember { mutableStateOf(false) }
    var vehicleMenu by remember { mutableStateOf(false) }

    suspend fun refresh() {
        val org = orgId ?: return
        recent = runCatching { api.recentInvoices(org) }.getOrDefault(recent)
    }

    LaunchedEffect(orgId) {
        val org = orgId ?: return@LaunchedEffect
        api.orgId = org
        vehicles = runCatching { api.vehicles(org).filter { it.isActive } }.getOrDefault(emptyList())
        vehicleId = null
        projects = runCatching { api.projects(org).filter { it.isActive } }.getOrDefault(emptyList())
        projectId = null
        while (true) { refresh(); delay(8000) } // statuses change as the server processes documents
    }

    fun send(file: CapturedFile) {
        val org = orgId ?: return
        val item = Pending(file, "A enviar…")
        queue.add(0, item)
        scope.launch {
            try {
                api.upload(org, file, vehicleId, paidPersonally, null, projectId)
                queue.remove(item)
                snackbar.showSnackbar("Enviado: ${file.fileName}")
                refresh()
            } catch (e: Exception) {
                val idx = queue.indexOf(item)
                if (idx >= 0) queue[idx] = item.copy(state = "Falhou — toque para repetir (${e.message})")
            }
        }
    }

    val capture = rememberDocumentCapture { file -> file?.let(::send) }
    val mailbox = me.memberships.firstOrNull { it.organizationId == orgId }?.mailbox

    Scaffold(
        topBar = {
            TopAppBar(title = { Text(orgs.firstOrNull { it.first == orgId }?.second ?: "A-Fatura") }, actions = {
                if (orgs.size > 1) {
                    TextButton(onClick = { orgMenu = true }) { Text("Trocar") }
                    DropdownMenu(orgMenu, { orgMenu = false }) {
                        orgs.forEach { (id, name) -> DropdownMenuItem(text = { Text(name) }, onClick = { orgId = id; orgMenu = false }) }
                    }
                }
                IconButton(onClick = onLogout) { Icon(Icons.Default.Logout, "Sair") }
            })
        },
        snackbarHost = { SnackbarHost(snackbar) },
    ) { padding ->
        LazyColumn(Modifier.fillMaxSize().padding(padding).padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            item {
                Spacer(Modifier.height(8.dp))
                Button(onClick = { capture.takePhoto() }, modifier = Modifier.fillMaxWidth().height(64.dp)) {
                    Icon(Icons.Default.CameraAlt, null); Spacer(Modifier.padding(4.dp)); Text("Fotografar fatura")
                }
                Spacer(Modifier.height(8.dp))
                OutlinedButton(onClick = { capture.pickFile() }, modifier = Modifier.fillMaxWidth()) {
                    Icon(Icons.Default.UploadFile, null); Spacer(Modifier.padding(4.dp)); Text("Escolher PDF ou imagem")
                }
            }
            item {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (vehicles.isNotEmpty()) {
                        AssistChip(onClick = { vehicleMenu = true }, label = { Text("Viatura: " + (vehicles.firstOrNull { it.id == vehicleId }?.plate ?: "automática")) })
                        DropdownMenu(vehicleMenu, { vehicleMenu = false }) {
                            DropdownMenuItem(text = { Text("Detetar automaticamente") }, onClick = { vehicleId = null; vehicleMenu = false })
                            vehicles.forEach { v -> DropdownMenuItem(text = { Text(v.plate) }, onClick = { vehicleId = v.id; vehicleMenu = false }) }
                        }
                    }
                    Checkbox(paidPersonally, { paidPersonally = it }); Text("Paguei eu (reembolso)", style = MaterialTheme.typography.bodySmall)
                }
                if (projects.isNotEmpty()) {
                    AssistChip(onClick = { projectMenu = true }, label = { Text("Projeto: " + (projects.firstOrNull { it.id == projectId }?.name ?: "nenhum")) })
                    DropdownMenu(projectMenu, { projectMenu = false }) {
                        DropdownMenuItem(text = { Text("Sem projeto") }, onClick = { projectId = null; projectMenu = false })
                        projects.forEach { p -> DropdownMenuItem(text = { Text(p.name) }, onClick = { projectId = p.id; projectMenu = false }) }
                    }
                }
                mailbox?.let { Text("Também pode reencaminhar faturas para: $it", style = MaterialTheme.typography.bodySmall) }
            }
            items(queue) { p ->
                Card(onClick = { if (p.state.startsWith("Falhou")) { queue.remove(p); send(p.file) } }, modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(12.dp)) { Text(p.file.fileName); Text(p.state, style = MaterialTheme.typography.bodySmall) }
                }
            }
            item { Text("Últimos documentos", style = MaterialTheme.typography.titleMedium) }
            items(recent, key = { it.id }) { inv ->
                Card(Modifier.fillMaxWidth()) {
                    Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(inv.supplierName ?: inv.fileName ?: "Documento")
                            Text(listOfNotNull(inv.issueDate, inv.categoryName, inv.vehiclePlate, if (inv.collectedInEFatura) "e-Fatura ✓" else null)
                                .joinToString(" · "), style = MaterialTheme.typography.bodySmall)
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text(inv.totalAmount?.let { "€ " + ((it * 100).toLong() / 100.0) } ?: "—")
                            Text(statusLabel(inv.status) + if (inv.flags.isNotEmpty()) " ⚠" else "", style = MaterialTheme.typography.bodySmall)
                        }
                    }
                }
            }
            item { Spacer(Modifier.height(24.dp)) }
        }
    }
}

private fun statusLabel(s: String) = when (s) {
    "Pending" -> "Em fila"; "Processing" -> "A processar"; "Processed" -> "Processada"; "NeedsReview" -> "Rever"; "Failed" -> "Erro"; else -> s
}

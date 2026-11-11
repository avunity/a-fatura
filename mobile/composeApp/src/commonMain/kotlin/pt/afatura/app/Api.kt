package pt.afatura.app

import com.russhwolf.settings.Settings
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.request.forms.formData
import io.ktor.client.request.forms.submitFormWithBinaryData
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.http.ContentType
import io.ktor.http.Headers
import io.ktor.http.HttpHeaders
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable data class LoginRequest(val email: String, val password: String)
@Serializable data class LoginResponse(val token: String)
@Serializable data class Membership(val organizationId: String, val name: String, val type: String, val role: String, val mailbox: String? = null)
@Serializable data class Client(val organizationId: String, val name: String)
@Serializable data class Me(val id: String, val email: String, val name: String, val role: String,
                            val memberships: List<Membership> = emptyList(), val clients: List<Client> = emptyList())
@Serializable data class Vehicle(val id: String, val plate: String, val isActive: Boolean = true)
@Serializable data class Project(val id: String, val name: String, val code: String? = null, val isActive: Boolean = true)
@Serializable data class InvoiceItem(
    val id: String, val status: String, val supplierName: String? = null, val fileName: String? = null,
    val issueDate: String? = null, val totalAmount: Double? = null, val categoryName: String? = null,
    val vehiclePlate: String? = null, val collectedInEFatura: Boolean = false, val flags: List<String> = emptyList(),
)
@Serializable data class InvoicePage(val total: Int, val items: List<InvoiceItem>)
@Serializable data class ApiErrorBody(val status: Int = 0, val title: String = "")

class ApiException(message: String) : Exception(message)

/** A file captured by the camera or picked from storage. */
class CapturedFile(val bytes: ByteArray, val fileName: String, val mimeType: String)

class Api(private val settings: Settings = Settings()) {
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }
    private val http = HttpClient { install(ContentNegotiation) { json(json) } }

    var baseUrl: String
        get() = settings.getString("baseUrl", DEFAULT_SERVER)
        set(v) = settings.putString("baseUrl", v.trimEnd('/'))
    var token: String?
        get() = settings.getStringOrNull("token")
        set(v) = if (v == null) settings.remove("token") else settings.putString("token", v)
    /** "system" | "light" | "dark" */
    var theme: String
        get() = settings.getString("theme", "system")
        set(v) = settings.putString("theme", v)
    var orgId: String?
        get() = settings.getStringOrNull("orgId")
        set(v) = if (v == null) settings.remove("orgId") else settings.putString("orgId", v)

    private suspend fun HttpResponse.ensureOk(): HttpResponse {
        if (status.isSuccess()) return this
        val msg = runCatching { body<ApiErrorBody>().title }.getOrNull()
        if (status.value == 401) token = null
        throw ApiException(msg?.takeIf { it.isNotBlank() } ?: "Erro ${status.value}")
    }

    suspend fun login(email: String, password: String) {
        val r = http.post("$baseUrl/api/auth/login") {
            contentType(ContentType.Application.Json)
            setBody(LoginRequest(email, password))
        }.ensureOk().body<LoginResponse>()
        token = r.token
    }

    suspend fun me(): Me = http.get("$baseUrl/api/auth/me") { auth() }.ensureOk().body()

    suspend fun vehicles(org: String): List<Vehicle> =
        http.get("$baseUrl/api/orgs/$org/vehicles") { auth() }.ensureOk().body()

    suspend fun projects(org: String): List<Project> =
        http.get("$baseUrl/api/orgs/$org/projects") { auth() }.ensureOk().body()

    suspend fun recentInvoices(org: String): List<InvoiceItem> =
        http.get("$baseUrl/api/orgs/$org/invoices?pageSize=30") { auth() }.ensureOk().body<InvoicePage>().items

    suspend fun upload(org: String, file: CapturedFile, vehicleId: String?, paidPersonally: Boolean, notes: String?, projectId: String? = null) {
        http.submitFormWithBinaryData(
            url = "$baseUrl/api/orgs/$org/invoices",
            formData = formData {
                append("source", "Mobile")
                vehicleId?.let { append("vehicleId", it) }
                projectId?.let { append("projectId", it) }
                if (paidPersonally) append("paidPersonally", "true")
                notes?.takeIf { it.isNotBlank() }?.let { append("notes", it) }
                append("files", file.bytes, Headers.build {
                    append(HttpHeaders.ContentType, file.mimeType)
                    append(HttpHeaders.ContentDisposition, "filename=\"${file.fileName}\"")
                })
            }
        ) { auth() }.ensureOk()
    }

    private fun io.ktor.client.request.HttpRequestBuilder.auth() {
        token?.let { header(HttpHeaders.Authorization, "Bearer $it") }
    }

    companion object {
        const val DEFAULT_SERVER = "https://fatura.avunity.com"
    }
}

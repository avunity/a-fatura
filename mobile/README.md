# A-Fatura — app mobile (Kotlin Multiplatform + Compose Multiplatform)

Android e iOS a partir do mesmo código (`composeApp/src/commonMain`):
- login (servidor configurável, por omissão `https://fatura.avunity.com`)
- escolher a entidade (empresas onde é membro, ou clientes se for contabilista)
- **digitalizar fatura**: scanner de documentos nativo (Android: Google ML Kit Document Scanner; iOS: VisionKit). Deteta os cantos da fatura, corta o fundo, corrige a perspetiva e melhora a imagem; 1 página → JPEG, várias → PDF. Sem Google Play services, usa a câmara normal
- **escolher PDF/imagem**, com a viatura opcional e a opção "paguei eu"
- envio com `source=Mobile`, lista dos últimos documentos com o estado do processamento (atualiza a cada 8 s)
- mostra o email pessoal para reencaminhar faturas

Código específico de cada plataforma: `Platform.android.kt` (TakePicture + FileProvider, OpenDocument) e `Platform.ios.kt` (UIImagePickerController, UIDocumentPicker).

## Correr
- **Android:** abre a pasta `mobile/` no Android Studio (com o plugin Kotlin Multiplatform) → executa `composeApp`.
  Ou: `./gradlew :composeApp:assembleDebug` (gera primeiro o wrapper com `gradle wrapper`).
- **iOS:** cria o projeto Xcode `iosApp` com o wizard KMP (kmp.jetbrains.com) ou copia o `iosApp/` de um template Compose Multiplatform. No `ContentView.swift`, usa `MainViewControllerKt.MainViewController()`.
  No `Info.plist`, acrescenta `NSCameraUsageDescription` e `NSPhotoLibraryUsageDescription`.

As versões estão em `gradle/libs.versions.toml`. Alinha-as com as do teu Android Studio ou template KMP, se for preciso.

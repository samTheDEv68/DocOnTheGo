# Doc On The Go

Telehealth platform that matches patients in remote and underserved areas with verified doctors, with an AI health assistant for symptom triage.

**Before going live, read [docs/Required-Inputs.pdf](docs/Required-Inputs.pdf).** It lists every API key, credential and decision the owner still has to provide.

## What's in the repo

| Project | Language | Purpose |
|---|---|---|
| `src/DocOnTheGo.Core` | **VB.NET** (.NET 9) | Shared rules: doctor tagging, `PatientID-#1234` tags, symptom triage engine with emergency red flags, proximity matching, validation. Used by the API **and inside the Android app**. |
| `src/DocOnTheGo.Api` | **VB.NET** (ASP.NET Core 9) | REST API: registration, SMS one-time-code log in + JWT, encrypted credential uploads, verification workflow, AI triage (optional Claude integration), appointments, messages, prescriptions, audit log. |
| `src/DocOnTheGo.Admin` | **VB.NET** WinForms (Windows) | Credential review console for the medical review board. |
| `src/DocOnTheGo.Mobile` | C# .NET MAUI (Android) | The APK. Screens follow the mockups. |
| `tests/DocOnTheGo.Core.Tests` | VB.NET xUnit | Tests for the core rules. |

### Why the Android app is C#

VB.NET cannot target Android: Microsoft's Android toolchain (.NET for Android / MAUI) only supports C# and F#. To keep the system VB.NET-first, all business logic lives in the VB.NET `DocOnTheGo.Core` library, which the C# app references and runs on the phone. The server and admin tools are VB.NET throughout.

## Try the app

1. Install `dist/DocOnTheGo.apk` (from the GitHub Actions artifact or the release) on an Android 7.0+ phone. You may need to allow "install unknown apps".
2. With no server configured the app runs in **offline demo mode**: data stays on the phone and eight fictional sample doctors are pre-loaded.
3. To use a real server, open *Profile → Change server* (or the link at the bottom of the sign-up screen) and enter the API's HTTPS address.

Demo flow: create a patient account → Home → AI Health Assistant → "I have a persistent headache and mild fever" → book a recommended doctor. To see the doctor side, log out, tap *Are you a doctor? Register here*, upload a degree and board certificate (PDF/JPEG/PNG), submit, then tap *Approve (demo)* to simulate the reviewer.

## Run the API

```bash
cd src/DocOnTheGo.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 dotnet run
```

Development settings (`appsettings.Development.json`) use a dev JWT key, admin key `dev-admin-key-change-me`, return OTP codes in the API response, and log SMS messages to the console. From the Android emulator the API is at `http://10.0.2.2:5080`.

Production configuration (environment variables use `__` for `:`):

| Setting | Purpose |
|---|---|
| `Jwt__SigningKey` | 32+ random characters (required) |
| `DocOnTheGo__AdminApiKey` | Key for the review console |
| `DocOnTheGo__FileEncryptionKeyBase64` | 32-byte AES key for stored certificates |
| `DocOnTheGo__SeedSampleDoctors` | Set `false` in production |
| `Ai__Provider`, `Ai__AnthropicApiKey`, `Ai__Model` | Optional LLM triage (`anthropic`); otherwise the rules engine |
| `Sms__Provider`, `Sms__Twilio*` | `twilio` to send real SMS |
| `CredentialRegistry__*` | External licence registry connector |

### Main endpoints

| Method | Path | Who |
|---|---|---|
| POST | `/api/auth/patients/register`, `/api/auth/doctors/register` | anyone |
| POST | `/api/auth/otp/request`, `/api/auth/otp/verify` | anyone |
| GET | `/api/doctors?q=&lat=&lon=` | anyone (verified doctors only) |
| POST | `/api/triage` | anyone |
| POST | `/api/doctors/me/credentials` (multipart), `/api/doctors/me/submit` | doctor |
| GET/POST/PATCH | `/api/appointments` | patient / doctor |
| GET/POST | `/api/conversations`, `/api/conversations/{id}/messages` | patient / doctor |
| GET/POST | `/api/prescriptions` | patient / doctor (issue) |
| GET/POST | `/api/admin/doctors`, `/api/admin/doctors/{id}/review`, `/api/admin/audit` | `X-Admin-Key` |

## Build the APK

Requirements: .NET 9 SDK, `dotnet workload install maui-android`, JDK 17+, Android SDK with platform 35.

```bash
./build-apk.sh                     # debug-key signed, for testing → dist/DocOnTheGo.apk
KEYSTORE=release.keystore KEY_ALIAS=doconthego KEY_PASS=... ./build-apk.sh   # release-signed
```

GitHub Actions (`.github/workflows/build.yml`) runs the tests and builds the APK on every push; download it from the run's *Artifacts*. Add the secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` to get release-signed builds.

## Known gaps

Not built yet because they depend on decisions in the PDF: video calls, map view, push notifications, a persistent database (the API currently stores data in memory), S3 storage, Auth0/Firebase sign-in, an Africa's Talking SMS adapter, and a real medical-board registry connector. The triage rules are not clinically validated.

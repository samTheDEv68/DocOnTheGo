"""Generates docs/Required-Inputs.pdf: every key, credential and decision the owner must supply.

Run: python3 docs/make_required_inputs_pdf.py   (needs: pip install reportlab)
"""
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (KeepTogether, PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table,
                                TableStyle)

OUT = Path(__file__).with_name("Required-Inputs.pdf")
TEAL = colors.HexColor("#1E6F78")
MINT = colors.HexColor("#E3F4F2")
INK = colors.HexColor("#1B2B2E")

ss = getSampleStyleSheet()
H1 = ParagraphStyle("H1", parent=ss["Title"], textColor=TEAL, fontSize=22, leading=26, alignment=TA_LEFT)
H2 = ParagraphStyle("H2", parent=ss["Heading2"], textColor=TEAL, spaceBefore=10, spaceAfter=4)
BODY = ParagraphStyle("Body", parent=ss["BodyText"], textColor=INK, fontSize=9.5, leading=13)
SMALL = ParagraphStyle("Small", parent=BODY, fontSize=8.5, leading=11)
CELL = ParagraphStyle("Cell", parent=BODY, fontSize=8.5, leading=11)
CELLB = ParagraphStyle("CellB", parent=CELL, fontName="Helvetica-Bold")


def p(text, style=BODY):
    return Paragraph(text, style)


def table(header, rows, widths):
    data = [[p(h, CELLB) for h in header]] + [[p(c, CELL) for c in r] for r in rows]
    t = Table(data, colWidths=widths, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), MINT),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#B9D9D6")),
        ("LEFTPADDING", (0, 0), (-1, -1), 4),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4),
    ]))
    return t


story = [
    p("Doc On The Go: what we need from you", H1),
    p("Keys, credentials and decisions required to take the app from working build to live service. "
      "Generated 12 November 2026 alongside the first build.", BODY),
    Spacer(1, 6),
    p("How to use this list", H2),
    p("Nothing here blocks you from installing and trying the APK today: with no server address set, the app runs in "
      "<b>offline demo mode</b> on the phone, using the same VB.NET logic as the server and eight fictional sample "
      "doctors. Items marked <b>Required</b> must be settled before real patients use the service. Secrets go in "
      "server configuration (environment variables or a secrets manager), never in the Git repository. For each "
      "setting the exact configuration name is given, e.g. <font face='Courier'>Jwt__SigningKey</font> as an "
      "environment variable or <font face='Courier'>Jwt:SigningKey</font> in appsettings.json.", BODY),

    p("1. What was built, and why this stack", H2),
    p("You asked for a VB.NET build shipped as an Android APK. VB.NET cannot compile an Android app: Microsoft's Android "
      "toolchain (.NET for Android / MAUI) supports C# and F# only. So the work is split to keep as much as possible in "
      "VB.NET, which also matches your architecture document (VB.NET admin and validator tier, mobile client separate):", BODY),
    table(["Part", "Language / framework", "What it does"], [
        ["DocOnTheGo.Core", "VB.NET class library (.NET 9)",
         "Shared business rules: doctor tagging (Generalist / GP, Specialist, Child Specialist, TagGP, TagCardio...), "
         "PatientID-#1234 generation, symptom triage engine with emergency red flags, geospatial doctor matching, "
         "input and credential-file validation. <b>Runs inside the APK and on the server.</b>"],
        ["DocOnTheGo.Api", "VB.NET, ASP.NET Core 9",
         "REST API: patient and doctor registration, passwordless SMS log in (OTP + JWT), credential upload (encrypted "
         "AES-256-GCM at rest), verification workflow, AI triage, appointments, messages, prescriptions, audit log."],
        ["DocOnTheGo.Admin", "VB.NET WinForms (.NET 9, Windows)",
         "Credential review console for the medical review board: list pending doctors, open documents, approve or "
         "reject, view audit log."],
        ["DocOnTheGo.Mobile", "C# .NET MAUI (Android, API 24+)",
         "The APK. Screens from your mockup: Create Account, Log in, Home with search and featured doctors, AI Health "
         "Assistant, Doctor Registration and Certification upload, booking, Appointments, Messages, Profile."],
    ], [32 * mm, 38 * mm, 100 * mm]),
    Spacer(1, 4),
    p("Your architecture document lists Flutter/React Native for mobile and Node.js/FastAPI for services. Those were "
      "replaced by .NET MAUI and a VB.NET API so the whole system is one .NET codebase with VB.NET at its core, as you "
      "asked. <b>Decision for you:</b> confirm this is acceptable, or tell us if the Flutter / Node.js split in the "
      "document is still a requirement.", BODY),

    p("2. Secrets you must generate (Required)", H2),
    p("These are random values you create yourself; no third-party account is needed.", BODY),
    table(["Setting", "What it is", "How to create it"], [
        ["Jwt:SigningKey", "Signs log-in tokens. Anyone holding it can impersonate any user.",
         "At least 32 random characters, e.g. <font face='Courier'>openssl rand -base64 48</font>"],
        ["DocOnTheGo:AdminApiKey", "Password the VB.NET review console sends in the X-Admin-Key header.",
         "<font face='Courier'>openssl rand -hex 32</font>; give it only to credential reviewers. "
         "(Later: replace with per-reviewer accounts and MFA, see section 4.)"],
        ["DocOnTheGo:FileEncryptionKeyBase64", "AES-256 key for doctor certificates stored on disk.",
         "<font face='Courier'>openssl rand -base64 32</font>. If unset, a temporary key is used and uploaded files "
         "become unreadable after a restart. Keep a secure backup: losing it loses every stored certificate."],
        ["Android release keystore", "Signs the APK so updates install over the old version and Play Store accepts it.",
         "<font face='Courier'>keytool -genkeypair -v -keystore release.keystore -alias doconthego -keyalg RSA "
         "-keysize 4096 -validity 10000</font>. The APK delivered now is signed with a throwaway debug key "
         "(fine for testing only). For GitHub Actions add secrets ANDROID_KEYSTORE_BASE64, ANDROID_KEY_ALIAS, "
         "ANDROID_KEY_PASSWORD."],
    ], [38 * mm, 52 * mm, 80 * mm]),

    p("3. Third-party accounts and API keys", H2),
    table(["Service", "Status in this build", "What we need from you", "Settings"], [
        ["AI symptom triage (LLM)", "Built-in offline rules engine works now. Claude (Anthropic) integration is "
         "written and switches on when a key is set; it falls back to the rules engine on any error and can never "
         "lower an emergency flagged by the rules.",
         "<b>Decide</b> whether to use an LLM at all for triage. If yes: an Anthropic API key, and confirm data "
         "handling is acceptable for patient symptom text (zero-retention / BAA terms with the provider). The spec "
         "mentions a fine-tuned model in HIPAA enclaves: tell us if that is a firm requirement.",
         "Ai:Provider = anthropic<br/>Ai:AnthropicApiKey<br/>Ai:Model (default claude-opus-5-5)"],
        ["SMS / OTP delivery", "Codes are written to the server log (development). A Twilio sender is implemented.",
         "<b>Decide</b> Twilio or Africa's Talking (spec lists both). For Twilio: Account SID, Auth Token, a sender "
         "number. Africa's Talking needs a small adapter (username + API key + sender ID); tell us if you want it.",
         "Sms:Provider = twilio<br/>Sms:TwilioAccountSid<br/>Sms:TwilioAuthToken<br/>Sms:TwilioFromNumber"],
        ["Credential verification registry", "Doctors upload degree and board certificate; files are type-checked, "
         "then wait for manual review in the VB.NET console. A registry connector stub exists.",
         "<b>Required:</b> which countries and medical boards you will verify against, and their API access. We could "
         "not confirm that the \"Global Medical Credentialing API\" named in the spec exists as a public service; "
         "please send the provider name, API docs and key. Also decide whether to use AWS Textract OCR (needs AWS "
         "credentials) for reading certificates.",
         "CredentialRegistry:Provider = registry<br/>CredentialRegistry:BaseUrl<br/>CredentialRegistry:ApiKey"],
        ["Identity / MFA (Auth0 or Firebase)", "Not used. The API has its own passwordless OTP log in with JWT.",
         "<b>Decide</b> whether to keep built-in log in or move to Auth0 / Firebase Auth (spec lists both, with MFA "
         "for doctors). If moving: tenant domain, client IDs, and for Firebase the google-services.json file.",
         "To be added"],
        ["Maps (Mapbox or Google Maps)", "Not used. Distance matching uses phone GPS and straight-line distance; no "
         "map is shown and no routes are drawn.",
         "<b>Decide</b> provider. Then an API key restricted to the app's package name com.doconthego.app.",
         "To be added"],
        ["Video consultations (Agora or Twilio Video)", "Not built. \"Online consultation\" bookings are created and "
         "patients and doctors can message each other, but there is no video call screen yet.",
         "<b>Decide</b> provider. Agora: App ID + App Certificate. Twilio Video: API Key SID + Secret.",
         "To be added"],
        ["Push notifications", "Not built (SMS is used for appointment and prescription alerts).",
         "Optional: a Firebase project for Cloud Messaging (google-services.json).", "To be added"],
    ], [28 * mm, 42 * mm, 62 * mm, 38 * mm]),

    p("4. Infrastructure decisions (Required before launch)", H2),
    table(["Item", "Now", "Needed from you"], [
        ["Database", "In-memory: all accounts, bookings and messages are lost when the API restarts.",
         "Approve PostgreSQL + PostGIS (per spec) and where it is hosted, then a connection string. The data layer is "
         "behind one interface (IDataStore), so this is a contained change."],
        ["File storage", "Encrypted files on the API server's disk (folder uploads-data).",
         "Approve AWS S3 (per spec): bucket name, region, and an IAM role or access keys; ideally a KMS key."],
        ["Hosting and domain", "Runs anywhere .NET 9 runs; nothing deployed.",
         "Where to host (spec says AWS GovCloud / HIPAA-eligible zones), the API domain name (e.g. api.yourdomain.com) "
         "and its TLS certificate. The app only allows HTTPS, except to a local test server."],
        ["Reviewer accounts", "One shared admin key.", "List of reviewers; decide if they sign in with Microsoft "
         "Entra ID / Auth0 with MFA."],
        ["Android publishing", "Side-loadable APK, package id com.doconthego.app, version 1.0.0.",
         "Confirm the package id (it cannot change after Play Store release), a Google Play developer account, and "
         "whether you also want an .aab for Play."],
    ], [32 * mm, 58 * mm, 80 * mm]),

    p("5. Product, clinical and legal decisions", H2),
    table(["Question", "Current assumption in the build"], [
        ["Which country or countries launch first?", "Sample doctors are placed around Nairobi and Kisumu, Kenya, and "
         "the emergency button dials 112. Tell us the launch region(s) and correct emergency numbers."],
        ["Which data-protection law applies?", "Spec says HIPAA principles. If you launch outside the US, local law "
         "applies too (e.g. Kenya Data Protection Act 2019, GDPR). A privacy policy and terms of use URL are needed "
         "for the app store listing and in-app consent."],
        ["Who signs off the triage rules clinically?", "The keyword ontology and red-flag list in "
         "SymptomTriageEngine.vb were written from general knowledge and are not clinically validated. A clinician "
         "must review them before patients rely on them."],
        ["Remove sample doctors?", "Eight fictional doctors are seeded so the app is not empty. Set "
         "DocOnTheGo:SeedSampleDoctors = false in production."],
        ["Doctor ratings", "Ratings are shown but patients cannot rate yet. Decide if and when patients rate doctors."],
        ["Who may issue prescriptions?", "Any verified doctor can issue one for their own appointment (API only; there "
         "is no prescription form in the app yet). Confirm local e-prescribing rules."],
        ["Branding", "A placeholder teal cross icon and the name \"Doc On The Go\". Send a logo (SVG preferred) and "
         "brand colours if different."],
        ["Consultation fees / payments", "Not in scope of the documents, so nothing is built. Tell us if bookings "
         "need payment (e.g. M-Pesa, card)."],
    ], [55 * mm, 115 * mm]),

    p("6. Access we still need", H2),
    p("GitHub: this session could not push to samTheDEv68/DocOnTheGo because no GitHub account is connected to "
      "Claude. Connect GitHub at claude.ai/connect-github and install the Claude GitHub App on the repository.", BODY),

    p("7. What was and was not tested", H2),
    p("Tested: the VB.NET core library (15 unit tests passing), the VB.NET API end to end over HTTP (register, triage, "
      "booking, doctor credential upload, submit, admin approval, tags assigned, OTP), the admin console compiles, and "
      "the APK builds, is signed, targets Android 15 (API 35) with minimum Android 7.0 (API 24) for arm64, armv7 and "
      "x86_64 phones. <b>Not tested:</b> the APK has not been launched on a phone or emulator, because the build "
      "machine had no Android emulator available. Please install it on a test phone first and report anything "
      "that looks wrong.", BODY),
]


def footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(colors.grey)
    canvas.drawString(15 * mm, 10 * mm, "Doc On The Go - Required inputs")
    canvas.drawRightString(195 * mm, 10 * mm, f"Page {doc.page}")
    canvas.restoreState()


SimpleDocTemplate(str(OUT), pagesize=A4, leftMargin=15 * mm, rightMargin=15 * mm, topMargin=15 * mm,
                  bottomMargin=16 * mm, title="Doc On The Go - Required Inputs",
                  author="Doc On The Go").build(story, onFirstPage=footer, onLaterPages=footer)
print(OUT)

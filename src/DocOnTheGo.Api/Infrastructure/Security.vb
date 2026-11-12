Option Strict On
Imports System.Collections.Concurrent
Imports System.IdentityModel.Tokens.Jwt
Imports System.IO
Imports System.Security.Claims
Imports System.Security.Cryptography
Imports System.Text
Imports Microsoft.Extensions.Configuration
Imports Microsoft.IdentityModel.Tokens

Namespace Infrastructure

    Public Class TokenService
        Private ReadOnly _cfg As IConfiguration

        Public Sub New(cfg As IConfiguration)
            _cfg = cfg
        End Sub

        Public Shared Function SigningKeyFrom(cfg As IConfiguration) As SymmetricSecurityKey
            Dim key = cfg("Jwt:SigningKey")
            If String.IsNullOrWhiteSpace(key) OrElse key.Length < 32 Then
                Throw New InvalidOperationException("Jwt:SigningKey must be set to at least 32 characters (see docs/Required-Inputs.pdf).")
            End If
            Return New SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        End Function

        Public Function Issue(userId As String, role As String, displayName As String) As String
            Dim hours = _cfg.GetValue(Of Integer)("Jwt:TokenLifetimeHours", 24)
            Dim claims = New List(Of Claim) From {
                New Claim(JwtRegisteredClaimNames.Sub, userId),
                New Claim(ClaimTypes.NameIdentifier, userId),
                New Claim(ClaimTypes.Role, role),
                New Claim(ClaimTypes.Name, displayName)
            }
            Dim creds As New SigningCredentials(SigningKeyFrom(_cfg), SecurityAlgorithms.HmacSha256)
            Dim token As New JwtSecurityToken(_cfg("Jwt:Issuer"), _cfg("Jwt:Audience"), claims,
                                              expires:=DateTime.UtcNow.AddHours(hours), signingCredentials:=creds)
            Return New JwtSecurityTokenHandler().WriteToken(token)
        End Function
    End Class

    ''' <summary>One-time passcodes for passwordless phone/email sign-in (5 minute lifetime, 5 attempts).</summary>
    Public Class OtpService
        Private Class Pending
            Public Code As String = ""
            Public ExpiresUtc As DateTime
            Public Attempts As Integer
        End Class

        Private ReadOnly _codes As New ConcurrentDictionary(Of String, Pending)(StringComparer.OrdinalIgnoreCase)

        Public Function Create(contact As String) As String
            Dim code = Core.Services.IdGenerator.NewOtp()
            _codes(contact.Trim()) = New Pending With {.Code = code, .ExpiresUtc = DateTime.UtcNow.AddMinutes(5)}
            Return code
        End Function

        Public Function Verify(contact As String, code As String) As Boolean
            Dim p As Pending = Nothing
            If Not _codes.TryGetValue(contact.Trim(), p) Then Return False
            p.Attempts += 1
            If p.ExpiresUtc < DateTime.UtcNow OrElse p.Attempts > 5 Then
                _codes.TryRemove(contact.Trim(), p)
                Return False
            End If
            Dim ok = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(p.Code), Encoding.ASCII.GetBytes(If(code, "").Trim()))
            If ok Then _codes.TryRemove(contact.Trim(), p)
            Return ok
        End Function
    End Class

    ''' <summary>Stores credential uploads encrypted at rest with AES-256-GCM.</summary>
    Public Interface IFileStore
        Function Save(data As Byte(), suggestedName As String) As String
        Function Load(storageKey As String) As Byte()
    End Interface

    Public Class EncryptedLocalFileStore
        Implements IFileStore

        Private ReadOnly _root As String
        Private ReadOnly _key As Byte()

        Public Sub New(cfg As IConfiguration)
            _root = Path.GetFullPath(If(cfg("DocOnTheGo:UploadsPath"), "uploads-data"))
            Directory.CreateDirectory(_root)
            Dim b64 = cfg("DocOnTheGo:FileEncryptionKeyBase64")
            If String.IsNullOrWhiteSpace(b64) Then
                ' Development fallback: a random per-process key (files become unreadable after restart).
                _key = RandomNumberGenerator.GetBytes(32)
            Else
                _key = Convert.FromBase64String(b64)
                If _key.Length <> 32 Then Throw New InvalidOperationException("DocOnTheGo:FileEncryptionKeyBase64 must decode to 32 bytes.")
            End If
        End Sub

        Public Function Save(data As Byte(), suggestedName As String) As String Implements IFileStore.Save
            Dim key = Core.Services.IdGenerator.NewId() & ".bin"
            Dim nonce = RandomNumberGenerator.GetBytes(12)
            Dim cipher(data.Length - 1) As Byte
            Dim tag(15) As Byte
            Using aes As New AesGcm(_key, 16)
                aes.Encrypt(nonce, data, cipher, tag)
            End Using
            Using fs = File.Create(Path.Combine(_root, key))
                fs.Write(nonce, 0, nonce.Length)
                fs.Write(tag, 0, tag.Length)
                fs.Write(cipher, 0, cipher.Length)
            End Using
            Return key
        End Function

        Public Function Load(storageKey As String) As Byte() Implements IFileStore.Load
            Dim safe = Path.GetFileName(storageKey)
            Dim all = File.ReadAllBytes(Path.Combine(_root, safe))
            Dim nonce = all.AsSpan(0, 12).ToArray()
            Dim tag = all.AsSpan(12, 16).ToArray()
            Dim cipher = all.AsSpan(28).ToArray()
            Dim plain(cipher.Length - 1) As Byte
            Using aes As New AesGcm(_key, 16)
                aes.Decrypt(nonce, cipher, tag, plain)
            End Using
            Return plain
        End Function
    End Class

End Namespace

Option Strict On
Imports System.Text.Json.Serialization
Imports DocOnTheGo.Api.Infrastructure
Imports DocOnTheGo.Core.Services
Imports Microsoft.AspNetCore.Authentication.JwtBearer
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.IdentityModel.Tokens

''' <summary>Doc On The Go REST API (VB.NET, ASP.NET Core 9).</summary>
Public Module Program

    Public Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)
        Dim cfg = builder.Configuration

        builder.Services.AddControllers().AddJsonOptions(
            Sub(o)
                o.JsonSerializerOptions.Converters.Add(New JsonStringEnumConverter())
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            End Sub)

        builder.Services.AddSingleton(Of IDataStore, InMemoryDataStore)()
        builder.Services.AddSingleton(Of IFileStore, EncryptedLocalFileStore)()
        builder.Services.AddSingleton(Of OtpService)()
        builder.Services.AddSingleton(Of TokenService)()
        builder.Services.AddHttpClient()
        builder.Services.AddHttpClient(Of ITriageService, TriageService)()

        If String.Equals(cfg("Sms:Provider"), "twilio", StringComparison.OrdinalIgnoreCase) Then
            builder.Services.AddHttpClient(Of ISmsSender, TwilioSmsSender)()
        Else
            builder.Services.AddSingleton(Of ISmsSender, ConsoleSmsSender)()
        End If

        If String.Equals(cfg("CredentialRegistry:Provider"), "registry", StringComparison.OrdinalIgnoreCase) Then
            builder.Services.AddHttpClient(Of ICredentialVerifier, RegistryCredentialVerifier)()
        Else
            builder.Services.AddSingleton(Of ICredentialVerifier, ManualReviewVerifier)()
        End If

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(
            Sub(o)
                o.TokenValidationParameters = New TokenValidationParameters With {
                    .ValidateIssuer = True, .ValidIssuer = cfg("Jwt:Issuer"),
                    .ValidateAudience = True, .ValidAudience = cfg("Jwt:Audience"),
                    .ValidateIssuerSigningKey = True, .IssuerSigningKey = TokenService.SigningKeyFrom(cfg),
                    .ValidateLifetime = True, .ClockSkew = TimeSpan.FromMinutes(2)}
            End Sub)
        builder.Services.AddAuthorization()
        builder.Services.AddCors(Sub(o) o.AddDefaultPolicy(Sub(p) p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()))

        Dim app = builder.Build()

        If cfg.GetValue(Of Boolean)("DocOnTheGo:SeedSampleDoctors") Then
            Dim store = app.Services.GetRequiredService(Of IDataStore)()
            For Each d In SeedData.SampleDoctors()
                store.SaveDoctor(d)
            Next
        End If

        app.UseCors()
        app.UseAuthentication()
        app.UseAuthorization()
        app.MapGet("/health", Function() New With {.status = "ok", .service = "DocOnTheGo.Api"})
        app.MapControllers()
        app.Run()
    End Sub

End Module

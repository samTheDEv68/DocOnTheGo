#!/usr/bin/env bash
# Builds the Doc On The Go Android APK.
# Needs: .NET 9 SDK, `dotnet workload install maui-android`, a JDK 17+ and the Android SDK (API 35).
# Usage: ./build-apk.sh                      (signed with a throwaway debug key, for testing)
#        KEYSTORE=release.keystore KEY_ALIAS=doconthego KEY_PASS=... ./build-apk.sh   (release signing)
set -euo pipefail
cd "$(dirname "$0")"
ANDROID_SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Android/Sdk}}"
ARGS=(-c Release -f net9.0-android -p:AndroidSdkDirectory="$ANDROID_SDK")
[[ -n "${JAVA_HOME:-}" ]] && ARGS+=(-p:JavaSdkDirectory="$JAVA_HOME")
if [[ -n "${KEYSTORE:-}" ]]; then
  ARGS+=(-p:AndroidKeyStore=true -p:AndroidSigningKeyStore="$KEYSTORE" -p:AndroidSigningKeyAlias="$KEY_ALIAS"
         -p:AndroidSigningKeyPass="env:KEY_PASS" -p:AndroidSigningStorePass="env:KEY_PASS")
fi
dotnet publish src/DocOnTheGo.Mobile/DocOnTheGo.Mobile.csproj "${ARGS[@]}" -o out/apk
mkdir -p dist
cp out/apk/*-Signed.apk dist/DocOnTheGo.apk
echo "APK: dist/DocOnTheGo.apk"

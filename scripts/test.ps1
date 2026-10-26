$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force .local/test-classes | Out-Null
$jdkPath = 'C:\Program Files\Java\jdk-17\bin'
& "$jdkPath\javac.exe" -d .local/test-classes android/src/main/java/com/jimu/sjm/Protocol.java android/src/main/java/com/jimu/sjm/Pairing.java tests/ProtocolTest.java
if ($LASTEXITCODE -ne 0) { throw 'Java test compile failed' }
& "$jdkPath\java.exe" -cp .local/test-classes ProtocolTest
if ($LASTEXITCODE -ne 0) { throw 'Java tests failed' }
if (-not (Test-Path .local/test-tls.p12)) {
    & "$jdkPath\keytool.exe" -genkeypair -keystore .local/test-tls.p12 -storetype PKCS12 -storepass test-only -keypass test-only -alias test -keyalg RSA -keysize 2048 -validity 30 -dname 'CN=SJM Test'
    if ($LASTEXITCODE -ne 0) { throw 'TLS test certificate creation failed' }
}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:.local\ClientTest.exe /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll desktop\RemoteClient.cs desktop\PointerSession.cs desktop\WheelGesture.cs tests\ClientTest.cs
if ($LASTEXITCODE -ne 0) { throw 'C# test compile failed' }
& .local\ClientTest.exe
if ($LASTEXITCODE -ne 0) { throw 'C# tests failed' }
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:UiStateTest /out:.local\UiStateTest.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll desktop\Program.cs desktop\PointerSession.cs desktop\RemoteClient.cs desktop\WheelGesture.cs tests\UiStateTest.cs
if ($LASTEXITCODE -ne 0) { throw 'UI state test compile failed' }
& .local\UiStateTest.exe
if ($LASTEXITCODE -ne 0) { throw 'UI state tests failed' }

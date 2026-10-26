$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
$sdkPath = Join-Path $env:LOCALAPPDATA 'Android\sdk'
if (-not (Test-Path $sdkPath)) { throw 'Android SDK not found' }
$env:JAVA_HOME = 'C:\Program Files\Java\jdk-17'
$env:ANDROID_HOME = $sdkPath
$gradleExe = Get-ChildItem (Join-Path $env:USERPROFILE '.gradle\wrapper\dists\gradle-8.14-all') -Recurse -Filter gradle.bat | Select-Object -First 1 -ExpandProperty FullName
if (-not $gradleExe) { throw 'Install Gradle 8.14 or edit the Gradle path in scripts/build.ps1' }
New-Item -ItemType Directory -Force .local,dist | Out-Null
$signFile = Join-Path $projectRoot '.local\sign-password.dpapi'
if (-not (Test-Path $signFile)) {
    $randomBytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($randomBytes); $rng.Dispose()
    [Convert]::ToBase64String($randomBytes) | ConvertTo-SecureString -AsPlainText -Force | ConvertFrom-SecureString | Set-Content $signFile
}
$secured = Get-Content $signFile | ConvertTo-SecureString
$env:SJM_SIGN_PASSWORD = [Net.NetworkCredential]::new('', $secured).Password
try {
    if (-not (Test-Path '.local\release.jks')) {
        & "$env:JAVA_HOME\bin\keytool.exe" -genkeypair -keystore .local/release.jks -storepass:env SJM_SIGN_PASSWORD -keypass:env SJM_SIGN_PASSWORD -alias sjm -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=SJM Local Release'
        if ($LASTEXITCODE -ne 0) { throw 'Signing key generation failed' }
    }
    & $gradleExe --no-daemon --console=plain :android:assembleRelease :android:lintRelease
    if ($LASTEXITCODE -ne 0) { throw 'Android build failed' }
    Copy-Item android/build/outputs/apk/release/android-release.apk dist/SJM-1.0.8.apk -Force
} finally { Remove-Item Env:SJM_SIGN_PASSWORD -ErrorAction SilentlyContinue }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /optimize+ /utf8output /out:dist\SJM.exe /win32manifest:desktop\app.manifest /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll desktop\Program.cs desktop\PointerSession.cs desktop\RemoteClient.cs desktop\WheelGesture.cs
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
Get-FileHash dist/SJM-1.0.8.apk,dist/SJM.exe -Algorithm SHA256 | Format-Table

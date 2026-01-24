$ErrorActionPreference = "Stop"

$projectPath = "$PSScriptRoot\..\EchoXIV.csproj"
$publishDir = "$PSScriptRoot\..\bin\Release"
$zipPath = "$PSScriptRoot\..\..\EchoXIV.zip"

Write-Host "🚧 Compilando EchoXIV en modo Release..." -ForegroundColor Cyan
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error en la compilación." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $publishDir)) {
    Write-Host "❌ No se encontró el directorio de salida: $publishDir" -ForegroundColor Red
    exit 1
}

Write-Host "📦 Creando archivo ZIP..." -ForegroundColor Cyan
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Comprimir los archivos del build
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

Write-Host "✅ ¡Listo!" -ForegroundColor Green
Write-Host "📂 Archivo creado: $zipPath" -ForegroundColor Yellow
Write-Host "🚀 AHORA: Sube este archivo a un nuevo Release en GitHub con el tag 'v0.0.100'." -ForegroundColor Magenta

# Script de instalación automática del plugin
# Ejecutar desde la carpeta del proyecto: .\install-plugin.ps1

param(
    [switch]$Clean = $false
)

$ErrorActionPreference = "Stop"

# Rutas
$projectPath = $PSScriptRoot
$buildPath = Join-Path $projectPath "bin\Release"
$devPluginsPath = Join-Path $env:APPDATA "XIVLauncher\devPlugins\FFXIVChatTranslator"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  FFXIV Chat Translator - Installer" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Paso 1: Verificar DLLs de Dalamud
Write-Host "[1/6] Verificando DLLs de Dalamud..." -ForegroundColor Yellow
$dalamudPath = Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev"

if (-not (Test-Path $dalamudPath)) {
    Write-Host "❌ ERROR: No se encontró la carpeta de Dalamud" -ForegroundColor Red
    Write-Host "   Ruta esperada: $dalamudPath" -ForegroundColor Red
    Write-Host "   Ejecuta FFXIV con XIVLauncher al menos una vez." -ForegroundColor Red
    exit 1
}

$requiredDlls = @("Dalamud.dll", "ImGui.NET.dll", "FFXIVClientStructs.dll")
$missingDlls = @()

foreach ($dll in $requiredDlls) {
    $dllPath = Join-Path $dalamudPath $dll
    if (-not (Test-Path $dllPath)) {
        $missingDlls += $dll
    }
}

if ($missingDlls.Count -gt 0) {
    Write-Host "❌ ERROR: Faltan DLLs de Dalamud:" -ForegroundColor Red
    $missingDlls | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "✅ DLLs de Dalamud encontradas" -ForegroundColor Green
Write-Host ""

# Paso 2: Limpiar (opcional)
if ($Clean) {
    Write-Host "[2/6] Limpiando build anterior..." -ForegroundColor Yellow
    & dotnet clean -c Release | Out-Null
    Write-Host "✅ Build limpiado" -ForegroundColor Green
} else {
    Write-Host "[2/6] Omitiendo limpieza (usa -Clean para limpiar)" -ForegroundColor Gray
}
Write-Host ""

# Paso 3: Compilar
Write-Host "[3/6] Compilando plugin..." -ForegroundColor Yellow
$buildOutput = & dotnet build -c Release 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ ERROR: La compilación falló" -ForegroundColor Red
    Write-Host ""
    Write-Host "Salida de compilación:" -ForegroundColor Red
    $buildOutput | Where-Object { $_ -match "error" } | ForEach-Object {
        Write-Host $_ -ForegroundColor Red
    }
    exit 1
}

Write-Host "✅ Compilación exitosa" -ForegroundColor Green
Write-Host ""

# Paso 4: Crear carpeta de plugins
Write-Host "[4/6] Preparando carpeta de plugins..." -ForegroundColor Yellow

if (Test-Path $devPluginsPath) {
    Write-Host "   Eliminando versión anterior..." -ForegroundColor Gray
    Remove-Item $devPluginsPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $devPluginsPath | Out-Null
Write-Host "✅ Carpeta creada: $devPluginsPath" -ForegroundColor Green
Write-Host ""

# Paso 5: Copiar archivos
Write-Host "[5/6] Copiando archivos del plugin..." -ForegroundColor Yellow

# Copiar DLL principal
$mainDll = Join-Path $buildPath "FFXIVChatTranslator.dll"
if (Test-Path $mainDll) {
    Copy-Item $mainDll -Destination $devPluginsPath
    Write-Host "   ✓ FFXIVChatTranslator.dll" -ForegroundColor Green
} else {
    Write-Host "   ❌ No se encontró FFXIVChatTranslator.dll" -ForegroundColor Red
    exit 1
}

# Copiar manifest
$manifest = Join-Path $projectPath "FFXIVChatTranslator.json"
if (Test-Path $manifest) {
    Copy-Item $manifest -Destination $devPluginsPath
    Write-Host "   ✓ FFXIVChatTranslator.json" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Advertencia: No se encontró FFXIVChatTranslator.json" -ForegroundColor Yellow
}

# Copiar Newtonsoft.Json
$newtonsoftDll = Join-Path $buildPath "Newtonsoft.Json.dll"
if (Test-Path $newtonsoftDll) {
    Copy-Item $newtonsoftDll -Destination $devPluginsPath
    Write-Host "   ✓ Newtonsoft.Json.dll" -ForegroundColor Green
}

# Copiar recursos
$resourcesPath = Join-Path $projectPath "Resources"
if (Test-Path $resourcesPath) {
    $destResources = Join-Path $devPluginsPath "Resources"
    Copy-Item $resourcesPath -Destination $devPluginsPath -Recurse -Force
    Write-Host "   ✓ Resources\" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Advertencia: No se encontró carpeta Resources" -ForegroundColor Yellow
}

Write-Host ""

# Paso 6: Resumen
Write-Host "[6/6] ✅ Instalación completada" -ForegroundColor Green
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Próximos pasos:" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Ejecuta FFXIV con XIVLauncher" -ForegroundColor White
Write-Host "2. En el juego, escribe: /xlsettings" -ForegroundColor White
Write-Host "3. Ve a 'Experimental' y activa 'Enable plugin testing'" -ForegroundColor White
Write-Host "4. En 'Dev Plugin Locations', agrega:" -ForegroundColor White
Write-Host "   %AppData%\XIVLauncher\devPlugins" -ForegroundColor Gray
Write-Host "5. Guarda y escribe: /xlplugins" -ForegroundColor White
Write-Host "6. Activa 'FFXIV Chat Translator'" -ForegroundColor White
Write-Host ""
Write-Host "Configuración: /translate config" -ForegroundColor Yellow
Write-Host ""
Write-Host "Plugin instalado en:" -ForegroundColor Gray
Write-Host $devPluginsPath -ForegroundColor Gray
Write-Host ""

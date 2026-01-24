# Setup del Plugin FFXIV Chat Translator

## ⚠️ Requisitos Previos

Necesitas tener **XIVLauncher instalado y haber ejecutado FFXIV al menos una vez** para que las DLLs de Dalamud estén disponibles.

## 📂 Paso 1: Verificar DLLs de Dalamud

Verifica que esta carpeta exista y contenga los archivos:

```
%AppData%\XIVLauncher\addon\Hooks\dev\
```

Debe contener:

- `Dalamud.dll`
- `ImGui.NET.dll`
- `FFXIVClientStructs.dll`
- `Lumina.dll`

**Si no existen**: Ejecuta FFXIV con XIVLauncher al menos una vez.

## 🔨 Paso 2: Compilar el Plugin

Abre PowerShell en la carpeta del proyecto y ejecuta:

```powershell
cd d:\Codigo\FFXIV\ffxiv-chat-translator\FFXIVChatTranslator
dotnet clean
dotnet build -c Release
```

**Si la compilación falla**,verificar que las rutas de las DLLs en `FFXIVChatTranslator.csproj` sean correctas:

```xml
<Reference Include="Dalamud">
  <HintPath>$(AppData)\XIVLauncher\addon\Hooks\dev\Dalamud.dll</HintPath>
  <Private>false</Private>
</Reference>
```

## 📦 Paso 3: Preparar Carpeta de Plugins

Crea la carpeta de plugins de desarrollo:

```powershell
$devPluginsPath = "$env:APPDATA\XIVLauncher\devPlugins\FFXIVChatTranslator"
New-Item -ItemType Directory -Force -Path $devPluginsPath
```

## 📋 Paso 4: Copiar Archivos Compilados

Copia los archivos compilados a la carpeta de plugins:

```powershell
# Copiar DLL principal
Copy-Item "bin\Release\FFXIVChatTranslator.dll" -Destination $devPluginsPath

# Copiar manifest
Copy-Item "FFXIVChatTranslator.json" -Destination $devPluginsPath

# Copiar dependencias (Newtonsoft.Json)
Copy-Item "bin\Release\Newtonsoft.Json.dll" -Destination $devPluginsPath -ErrorAction SilentlyContinue

# Copiar recursos
Copy-Item "Resources" -Destination $devPluginsPath -Recurse -Force
```

**O usa el script automático**: `.\install-plugin.ps1`

## 🎮 Paso 5: Configurar Dalamud

1. Ejecuta FFXIV con XIVLauncher
2. En el juego, escribe:
   ```
   /xlsettings
   ```
3. Ve a la pestaña **"Experimental"**
4. Activa **"Enable plugin testing"**
5. En **"Dev Plugin Locations"**, agrega (si no está ya):
   ```
   %AppData%\XIVLauncher\devPlugins
   ```
6. Haz clic en **"Save and Close"**

## ✅ Paso 6: Activar el Plugin

1. Escribe en el chat:
   ```
   /xlplugins
   ```
2. Busca **"FFXIV Chat Translator"** en la lista
3. Marca el checkbox para activarlo

## 🔧 Configuración Inicial

Una vez activado:

1. Abre configuración:

   ```
   /translate config
   ```

2. Configura:
   - **Idioma Origen**: Español (o tu idioma)
   - **Idioma Destino**: Inglés (o el que prefieras)
   - Activa **"Traducción Habilitada"**

## 🌐 Uso con Chat2

Si tienes Chat2 instalado:

1. Click derecho en cualquier mensaje de Chat2
2. Verás **"🌐 Traducir a..."** en el menú
3. Selecciona el idioma destino
4. ✅ Activa **"✓ Traducción Habilitada"**

Ahora escribe mensajes y se traducirán automáticamente.

## 🐛 Solución de Problemas

### El plugin no aparece en /xlplugins

- Verifica que los archivos estén en: `%AppData%\XIVLauncher\devPlugins\FFXIVChatTranslator\`
- Asegúrate de tener "Enable plugin testing" activado
- Reinicia el juego

### Error al compilar: "No se encuentra el ensamblado"

- Ejecuta FFXIV con XIVLauncher al menos una vez
- Verifica que `%AppData%\XIVLauncher\addon\Hooks\dev\` contenga las DLLs

### El plugin se carga pero da error

- Revisa los logs de Dalamud: `/xllog`
- Busca errores relacionados con "FFXIVChatTranslator"

### La traducción no funciona

⚠️ **IMPORTANTE**: El hook a `ChatBox.SendMessageUnsafe` aún no está implementado.

Por ahora el plugin puede:

- ✅ Detectar Chat2
- ✅ Mostrar menú contextual
- ✅ Cambiar configuración
- ❌ **No traduce mensajes aún** (requiere completar el hook)

## 📝 Notas Importantes

- Este plugin está en desarrollo
- La funcionalidad principal (traducción) requiere implementar el hook a `SendMessageUnsafe`
- Puedes usar el plugin para probar la UI y configuración
- El código de traducción (`GoogleTranslatorService`) está completo y funcional

## 🔄 Actualizar el Plugin

Si haces cambios en el código:

1. Recompila:

   ```powershell
   dotnet build -c Release
   ```

2. Copia archivos nuevamente (usa `install-plugin.ps1`)

3. Recarga plugins en el juego:
   ```
   /xlplugins
   ```
   Desactiva y reactiva el plugin

---

**¿Problemas?** Revisa los logs con `/xllog` en el juego.

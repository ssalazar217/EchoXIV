# Reglas del Proyecto: Chat2 Translator

## 🔧 Comandos Auto-Ejecutables (Sin Confirmación)

Los siguientes comandos deben ejecutarse **automáticamente sin pedir confirmación** al usuario:

### 1. Compilación

```powershell
dotnet build -c Release
```

**Razón**: Comando seguro de solo lectura que genera archivos en `/bin/Release/`

### 2. Copia de Archivos del Plugin

```powershell
Copy-Item "FFXIVChatTranslator\bin\Release\FFXIVChatTranslator.dll" -Destination "$env:APPDATA\XIVLauncher\devPlugins\FFXIVChatTranslator\" -Force

Copy-Item "FFXIVChatTranslator\FFXIVChatTranslator.json" -Destination "$env:APPDATA\XIVLauncher\devPlugins\FFXIVChatTranslator\" -Force
```

**Razón**:

- Copia archivos dentro del workspace del proyecto
- Destino es la carpeta de desarrollo de Dalamud (controlada por el usuario)
- Operación de deployment estándar

### 3. Comandos de Listado/Lectura

```powershell
Get-ChildItem
Get-Content
ls, dir, cat
```

**Razón**: Comandos de solo lectura sin efectos secundarios

---

## 📁 Rutas del Proyecto

### Código Fuente

```
d:\Codigo\FFXIV\ffxiv-chat-translator\FFXIVChatTranslator\
```

### Deployment (Dalamud DevPlugins)

```
%APPDATA%\XIVLauncher\devPlugins\FFXIVChatTranslator\
```

Estas dos rutas están permitidas para operaciones de copia automáticas.

---

## 🎯 Workflow de Desarrollo

1. Editar código en `d:\Codigo\FFXIV\ffxiv-chat-translator\`
2. Compilar con `dotnet build -c Release`
3. Copiar automáticamente `.dll` y `.json` a `devPlugins`
4. Usuario recarga plugin en juego con `/xlplugins`

**Todos los pasos 1-3 deben ser automáticos (SafeToAutoRun: true)**

---

## ⚠️ Comandos que SÍ Requieren Confirmación

- Eliminar archivos (`Remove-Item`, `del`)
- Modificar archivos fuera del workspace
- Instalación de paquetes del sistema
- Comandos de red externos (excepto `dotnet restore`)

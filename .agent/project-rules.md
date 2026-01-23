# Reglas del Proyecto: Chat2 Translator

## 🔧 Comandos Auto-Ejecutables (Sin Confirmación)

Los siguientes comandos deben ejecutarse **automáticamente sin pedir confirmación** al usuario:

### 1. Compilación

```powershell
dotnet build
dotnet build -c Release
dotnet build -c Debug
```

**Razón**: Comando seguro de solo lectura que genera archivos en `/bin/Release/`

### 2. Comandos de Listado/Lectura

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

---

## 🎯 Workflow de Desarrollo

1. Editar código en `d:\Codigo\FFXIV\ffxiv-chat-translator\`
2. Compilar con `dotnet build -c Release`
3. Usuario recarga plugin en juego con `/xlplugins`

**Todos los pasos 1-2 deben ser automáticos (SafeToAutoRun: true)**

---

## ⚠️ Comandos que SÍ Requieren Confirmación

- Eliminar archivos (`Remove-Item`, `del`)
- Modificar archivos fuera del workspace
- Instalación de paquetes del sistema
- Comandos de red externos (excepto `dotnet restore`)

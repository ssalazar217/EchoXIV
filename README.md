# FFXIV Chat Translator

Plugin para XIVLauncher/Dalamud que traduce automáticamente los mensajes que escribes en el chat antes de enviarlos, usando Google Translate sin necesidad de API key.

## 🌟 Características

- **Integración con Chat2**: Se integra perfectamente con el plugin Chat2 si lo tienes instalado
- **Traducción Automática**: Traduce tus mensajes antes de enviarlos al chat
- **Sin API Key**: Usa el método de TataruHelper para acceder a Google Translate móvil sin límites
- **Selector de Idioma Rápido**: Selector integrado en el menú contextual de Chat2
- **Soporte Multiidioma**: Español, Inglés, Japonés, Alemán, Francés, Portugués, Ruso, Chino, Coreano e Italiano
- **Configuración Persistente**: Guarda tus preferencias de idioma
- **Modo Fallback**: Funciona con o sin Chat2 instalado

## 📋 Requisitos

- FFXIV instalado con [XIVLauncher](https://goatcorp.github.io/)
- .NET 8.0 Runtime
- Windows (64-bit)
- **Recomendado**: Plugin [Chat2](https://github.com/Infiziert90/ChatTwo) instalado

## 🚀 Instalación

### Método 1: Desde el Repositorio de Plugins de Dalamud (Recomendado)

1. Abre FFXIV con XIVLauncher
2. Escribe `/xlplugins` en el chat
3. Busca "FFXIV Chat Translator"
4. Haz clic en "Install"

> ⚠️ **Nota**: El plugin aún no está disponible en el repositorio oficial. Usa el Método 2 por ahora.

### Método 2: Instalación Manual

1. **Descarga el plugin compilado** desde [Releases](../../releases)

2. **Localiza la carpeta de plugins de Dalamud**:

   ```
   %AppData%\XIVLauncher\devPlugins
   ```

   Si la carpeta `devPlugins` no existe, créala.

3. **Extrae el plugin**:
   - Crea una carpeta llamada `FFXIVChatTranslator` dentro de `devPlugins`
   - Extrae todos los archivos descargados en esa carpeta

4. **Habilita plugins en desarrollo**:
   - Escribe `/xldev` en el chat del juego
   - Activa "Enable plugin testing"

5. **Reinicia el juego** o escribe `/xlplugins` y activa el plugin manualmente

## 📖 Uso

### Con Chat2 Instalado (Modo Recomendado)

Si tienes Chat2 instalado, el plugin se integra automáticamente:

1. **Cambiar idioma destino**:
   - Haz clic derecho en cualquier mensaje de Chat2
   - Selecciona "🌐 Traducir a..."
   - Elige el idioma destino

2. **Toggle de traducción**:
   - En el mismo menú, activa/desactiva "✓ Traducción Habilitada"

3. **Configurar idioma origen**:

   ```
   /translate config
   ```

4. **Escribe y envía**:
   - Escribe normalmente en español (o tu idioma configurado)
   - El mensaje se traducirá automáticamente antes de enviarse
   - ✅ Los demás jugadores verán el mensaje traducido

### Sin Chat2 (Modo Fallback)

Si no tienes Chat2, el plugin usa un selector de idioma flotante:

1. **Selector de idioma**: Aparecerá un combobox pequeño en pantalla
2. **Cambiar posición**: Arrastra el selector donde prefieras
3. **Cambiar idioma**: Click en el combobox y selecciona el idioma

### Comandos Disponibles

| Comando              | Descripción                        |
| -------------------- | ---------------------------------- |
| `/translate` o `/tl` | Abre la ventana de configuración   |
| `/translate config`  | Abre la ventana de configuración   |
| `/translate on`      | Activa la traducción automática    |
| `/translate off`     | Desactiva la traducción automática |

## 🔧 Configuración

### Ventana de Configuración

Accede con `/translate config`:

- **Idioma Origen**: El idioma en el que escribes normalmente (ej. Español)
- **Idioma Destino**: Idioma al que se traducirán tus mensajes por defecto
- **Activar Traducción**: Toggle global para activar/desactivar
- **Preferir Chat2**: Si detecta Chat2, úsalo en lugar del modo independiente

### Ejemplo de Uso

**Tú escribes** (español):

```
Hola, ¿alguien quiere hacer dungeons?
```

**El juego envía** (inglés):

```
Hello, does anyone want to do dungeons?
```

**Otros jugadores ven**: El mensaje en inglés ✅

## 🐛 Solución de Problemas

### El plugin no aparece en la lista

1. Verifica que has copiado todos los archivos a la carpeta correcta
2. Habilita "Enable plugin testing" en `/xldev`
3. Reinicia el juego

### La traducción no funciona

1. Verifica que la traducción esté activada:
   - Con Chat2: Click derecho → "✓ Traducción Habilitada"
   - Sin Chat2: `/translate on`
2. Asegúrate de que el idioma origen y destino sean diferentes
3. Comprueba que tienes conexión a internet

### El selector de idioma no aparece (sin Chat2)

1. Abre la configuración con `/translate config`
2. Activa "Mostrar Selector de Idioma"
3. Si sigue sin aparecer, resetea su posición con el botón en la configuración

### Chat2 no se detecta

1. Asegúrate de tener Chat2 instalado y activo
2. Reinicia el plugin (`/xlplugins` → desactivar → activar)
3. Verifica en los logs que aparezca "✅ Chat2 detectado"

## 🎯 Características Avanzadas

### Integración con Chat2

Cuando Chat2 está instalado:

- ✅ No crea UI adicional (usa el chat de Chat2)
- ✅ Selector de idioma en el menú contextual
- ✅ Mantiene todas las features de Chat2 (tabs, URLs, selección de texto, etc.)
- ✅ Más ligero y eficiente

### Modo Independiente

Si Chat2 no está disponible:

- Widget flotante con selector de idioma
- Interceptación del chat nativo de FFXIV
- Funcionalidad completa de traducción

## 🤝 Contribuir

Las contribuciones son bienvenidas. Para cambios importantes:

1. Fork el repositorio
2. Crea una rama para tu feature (`git checkout -b feature/AmazingFeature`)
3. Commit tus cambios (`git commit -m 'Add some AmazingFeature'`)
4. Push a la rama (`git push origin feature/AmazingFeature`)
5. Abre un Pull Request

## 🙏 Agradecimientos

- **TataruHelper**: Por el método de traducción de Google sin API key
- **Chat2**: Por el excelente plugin de chat y su sistema IPC
- **Dalamud Team**: Por el framework de plugins de FFXIV
- **goatcorp**: Por XIVLauncher

## ⚠️ Disclaimer

Este plugin interactúa con el cliente de FFXIV a través de Dalamud. El uso de plugins de terceros puede violar los términos de servicio de Square Enix. Usa bajo tu propio riesgo.

## 📄 Licencia

Este proyecto está bajo la licencia MIT. Ver el archivo `LICENSE` para más detalles.

---

**¿Preguntas o problemas?** Abre un [Issue](../../issues) en GitHub.

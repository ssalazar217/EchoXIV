# <img src="https://raw.githubusercontent.com/ssalazar217/EchoXIV/master/EchoXIV/images/icon.png" width="48" height="48" align="center" /> EchoXIV 🌸 [![Discord](https://img.shields.io/discord/1464143706616627316?label=Discord&logo=discord&logoColor=white&color=7289da)](https://discord.gg/B3qnvuhN9G)

**EchoXIV** es un plugin avanzado para XIVLauncher/Dalamud diseñado para romper las barreras del idioma en FFXIV. A diferencia de otros traductores, EchoXIV se enfoca en la **comunicación activa**: permitiéndote ser entendido en cualquier idioma de forma fluida y natural.

## 🌟 Características Principales

- **Tu Mensaje en Cualquier Idioma (Outgoing Translation)**: Usa el comando `/tl <mensaje>` para enviar una traducción instantánea al canal activo.
- **Traducción Bidireccional**: Entiende lo que dicen y asegúrate de que te entiendan.
- **Detección Dinámica de Canales**: El sistema "No-Prefix" detecta automáticamente si estás en Party, FC, Say o en un Tell privado.
- **Superposición Nativa (WPF)**: Una ventana elegante y fluida que se integra perfectamente con el juego.
- **Visibilidad Inteligente (Smart Visibility)**: El plugin se oculta automáticamente durante cinemáticas, pantallas de carga y cuando el juego pierde el foco.
- **Privacidad y Simplicidad**: Soporte para **Google Translate** (sin API key) y **DeepL** (usado por profesionales) para una traducción de alta fidelidad.
- **Diferentes Modos de Ventana**: Elige el que mejor se adapte a tu setup técnico.

### 🖥️ Comparativa de Modos de Ventana

| Característica  | Ventana Interna (ImGui)                 | Ventana Nativa (WPF)               |
| :-------------- | :-------------------------------------- | :--------------------------------- |
| **Integración** | Total (dentro del juego)                | Ventana flotante independiente     |
| **Rendimiento** | Puede afectar FPS en monitores externos | **Sin impacto en FPS del juego**   |
| **Uso Ideal**   | Un solo monitor / Modo inmersivo        | Multi-monitor / Máximo rendimiento |
| **Tecnología**  | Dalamud WindowSystem                    | .NET WPF (Nativo Windows)          |

## 📷 Capturas de Pantalla

|              Ventana Nativa (WPF)              |           Ventana Interna (ImGui)            |
| :--------------------------------------------: | :------------------------------------------: |
| ![Native Chat](EchoXIV/images/native_chat.png) | ![ImGui Chat](EchoXIV/images/imgui_chat.png) |

|            Configuración             |
| :----------------------------------: |
| ![Config](EchoXIV/images/config.png) |

## 🚀 Instalación Rápida

1. Abre **XIVLauncher** (o el menú `/xlsettings` dentro del juego).
2. Ve a la pestaña **Experimental** -> **Custom Plugin Repositories**.
3. Añade la siguiente URL:
   ```
   https://raw.githubusercontent.com/ssalazar217/EchoXIV/master/repo.json
   ```
4. Guarda los cambios.
5. Busca **EchoXIV** en la lista de plugins disponibles e instálalo.

## 📖 Comandos

| Comando         | Descripción                                        |
| --------------- | -------------------------------------------------- |
| `/echoxiv`      | Abre la ventana de configuración.                  |
| `/tl <mensaje>` | Traduce y envía el mensaje al canal activo actual. |
| `/tl config`    | Acceso rápido a las opciones.                      |

## 🔧 Configuración

Accede al menú con `/echoxiv`:

- **General**: Configura tus idiomas de origen y destino.
- **Visuales**: Ajusta la opacidad, el bloqueo de ventana y el modo "Smart Visibility".
- **Filtros**: Excluye mensajes o canales que no desees traducir.

## 🙏 Agradecimientos

- **TataruHelper**: Por la inspiración técnica en el método de traducción.
- **Dalamud/XIVLauncher**: Por el increíble ecosistema de desarrollo.

---

**Nota**: Este plugin es una herramienta de terceros. Úsalo con responsabilidad y respeta los términos de servicio de Square Enix.

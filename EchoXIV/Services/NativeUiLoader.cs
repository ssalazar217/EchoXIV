using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;

namespace EchoXIV.Services
{
    /// <summary>
    /// Cargador dinámico para evitar dependencias estáticas de WPF que rompen el CI de Linux.
    /// </summary>
    public static class NativeUiLoader
    {
        private static bool _assembliesLoaded = false;
        private static readonly Dictionary<string, Assembly> _loadedAssemblies = new();

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool TryLoadWpf(Dalamud.Plugin.Services.IPluginLog logger)
        {
            if (!IsWindows) return false;
            if (_assembliesLoaded) return true;

            try
            {
                // Cargar assemblies clave de WPF del sistema
                LoadAssembly("WindowsBase", logger);
                LoadAssembly("PresentationCore", logger);
                LoadAssembly("PresentationFramework", logger);
                LoadAssembly("System.Xaml", logger);

                _assembliesLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error cargando librerías de WPF dinámicamente");
                return false;
            }
        }

        private static void LoadAssembly(string name, Dalamud.Plugin.Services.IPluginLog logger)
        {
            try 
            {
                var assembly = Assembly.Load(name);
                _loadedAssemblies[name] = assembly;
            }
            catch (Exception ex)
            {
                logger.Warning($"[NativeUiLoader] Error al cargar {name}: {ex.Message}");
                throw;
            }
        }

        public static object? CreateInstance(string assemblyName, string typeName, params object[] args)
        {
            if (!_loadedAssemblies.TryGetValue(assemblyName, out var assembly))
                assembly = Assembly.Load(assemblyName);

            var type = assembly.GetType(typeName);
            if (type == null) return null;

            return Activator.CreateInstance(type, args);
        }

        public static object? LoadXaml(string xamlContent)
        {
            // XamlReader.Parse(xamlContent)
            var xamlReaderType = Assembly.Load("PresentationFramework").GetType("System.Windows.Markup.XamlReader");
            var parseMethod = xamlReaderType?.GetMethod("Parse", new[] { typeof(string) });
            return parseMethod?.Invoke(null, new object[] { xamlContent });
        }
        
        public static object? GetEnumValue(string assemblyName, string typeName, object value)
        {
            try
            {
                if (!_loadedAssemblies.TryGetValue(assemblyName, out var assembly))
                    assembly = Assembly.Load(assemblyName);

                var type = assembly.GetType(typeName);
                if (type == null || !type.IsEnum) return value;

                if (value is string strValue)
                    return Enum.Parse(type, strValue);
                
                return Enum.ToObject(type, value);
            }
            catch
            {
                return value;
            }
        }
        
        public static string GetEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return string.Empty;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

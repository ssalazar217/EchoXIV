using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace EchoXIV.Services
{
    public class MessageHistoryManager : IDisposable
    {
        private const int SaveDebounceMs = 3000;

        private readonly List<TranslatedChatMessage> _messages = new();
        private readonly object _lock = new();
        private readonly Configuration _configuration;
        private readonly string? _configDir;
        private readonly string? _historyPath;
        private readonly System.Threading.Timer? _saveTimer;
        private readonly object _saveGate = new();
        private bool _dirty;

        public event Action<TranslatedChatMessage>? OnMessageAdded;
        public event Action<TranslatedChatMessage>? OnMessageUpdated;
        public event Action? OnHistoryCleared;

        public MessageHistoryManager(Configuration configuration, string? configDir = null)
        {
            _configuration = configuration;
            _configDir = configDir;
            
            if (_configDir != null)
            {
                _historyPath = Path.Combine(_configDir, "history.json");
                LoadHistory();
                _saveTimer = new System.Threading.Timer(_ => FlushSave(), null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void AddMessage(TranslatedChatMessage message)
        {
            lock (_lock)
            {
                _messages.Add(message);
                PruneMessagesBySize();
            }
            ScheduleSave();
            OnMessageAdded?.Invoke(message);
        }

        public void UpdateMessage(TranslatedChatMessage message)
        {
            lock (_lock)
            {
                var existing = _messages.FirstOrDefault(m => m.Id == message.Id);
                if (existing != null)
                {
                    existing.TranslatedText = message.TranslatedText;
                    existing.RomanizedText = message.RomanizedText;
                    existing.OriginalScript = message.OriginalScript;
                    existing.IsTranslating = false;
                }
                else
                {
                    // New message (e.g., outgoing with fresh Id)
                    _messages.Add(message);
                    PruneMessagesBySize();
                }
            }
            ScheduleSave();
            OnMessageUpdated?.Invoke(message);
        }

        public List<TranslatedChatMessage> GetHistory()
        {
            lock (_lock)
            {
                return _messages.ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _messages.Clear();
            }
            SaveHistory();
            OnHistoryCleared?.Invoke();
        }

        public void Dispose()
        {
            FlushSave();
            _saveTimer?.Dispose();
        }

        private void ScheduleSave()
        {
            if (_saveTimer == null) return;
            lock (_saveGate)
            {
                _dirty = true;
                try
                {
                    _saveTimer.Change(SaveDebounceMs, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void FlushSave()
        {
            lock (_saveGate)
            {
                if (!_dirty) return;
                _dirty = false;
            }
            SaveHistory();
        }

        private void LoadHistory()
        {
            if (_historyPath == null || !File.Exists(_historyPath)) return;

            try
            {
                var json = File.ReadAllText(_historyPath);
                var loadedMessages = JsonConvert.DeserializeObject<List<TranslatedChatMessage>>(json);
                if (loadedMessages != null)
                {
                    // Normalize messages stuck translating after plugin close.
                    foreach (var m in loadedMessages)
                    {
                        if (m.IsTranslating)
                        {
                            m.IsTranslating = false;
                            if (string.IsNullOrWhiteSpace(m.TranslatedText))
                                m.TranslatedText = m.OriginalText;
                        }
                    }
                    lock (_lock)
                    {
                        _messages.Clear();
                        _messages.AddRange(loadedMessages);
                        PruneMessagesBySize();
                    }
                    SaveHistory();
                }
            }
            catch (Exception)
            {
                // Silently fail to avoid crashing the plugin
            }
        }

        public void SaveHistory()
        {
            if (_historyPath == null) return;

            try
            {
                string json;
                lock (_lock)
                {
                    json = JsonConvert.SerializeObject(_messages, Formatting.Indented);
                }
                var temporaryPath = _historyPath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _historyPath, true);
            }
            catch (Exception)
            {
                // Silently fail
            }
        }

        public void TrimToConfiguredSize()
        {
            lock (_lock)
            {
                PruneMessagesBySize();
            }
            SaveHistory();
        }

        private void PruneMessagesBySize()
        {
            var maxBytes = (long)Math.Clamp(_configuration.MaxHistorySizeMb, 1, 1024) * 1024 * 1024;
            while (_messages.Count > 0)
            {
                var json = JsonConvert.SerializeObject(_messages, Formatting.None);
                if (Encoding.UTF8.GetByteCount(json) <= maxBytes)
                    break;

                _messages.RemoveAt(0);
            }
        }
    }
}

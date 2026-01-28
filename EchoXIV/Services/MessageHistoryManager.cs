using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;

namespace EchoXIV.Services
{
    public class MessageHistoryManager
    {
        private readonly List<TranslatedChatMessage> _messages = new();
        private readonly object _lock = new();
        private readonly Configuration _configuration;
        private readonly string? _configDir;
        private readonly string? _historyPath;

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
            }
        }

        public void AddMessage(TranslatedChatMessage message)
        {
            lock (_lock)
            {
                _messages.Add(message);
                PruneMessages();
            }
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
                    existing.IsTranslating = false;
                }
            }
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
                SaveHistory();
            }
            OnHistoryCleared?.Invoke();
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
                    lock (_lock)
                    {
                        _messages.Clear();
                        _messages.AddRange(loadedMessages);
                    }
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
                File.WriteAllText(_historyPath, json);
            }
            catch (Exception)
            {
                // Silently fail
            }
        }

        private void PruneMessages()
        {
            while (_messages.Count > _configuration.MaxDisplayedMessages)
            {
                _messages.RemoveAt(0);
            }
            SaveHistory();
        }
    }
}

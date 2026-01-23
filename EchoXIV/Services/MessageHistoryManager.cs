using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoXIV.Services
{
    public class MessageHistoryManager
    {
        private readonly List<TranslatedChatMessage> _messages = new();
        private readonly object _lock = new();
        private readonly Configuration _configuration;

        public event Action<TranslatedChatMessage>? OnMessageAdded;
        public event Action<TranslatedChatMessage>? OnMessageUpdated;
        public event Action? OnHistoryCleared;

        public MessageHistoryManager(Configuration configuration)
        {
            _configuration = configuration;
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
            }
            OnHistoryCleared?.Invoke();
        }

        private void PruneMessages()
        {
            while (_messages.Count > _configuration.MaxDisplayedMessages)
            {
                _messages.RemoveAt(0);
            }
        }
    }
}

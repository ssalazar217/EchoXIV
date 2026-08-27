using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services
{
    /// <summary>
    /// One Gemini model as returned by the cloud ListModels API. No local data.
    /// </summary>
    public sealed record GeminiModelInfo(
        string Id,
        string DisplayName,
        bool Translatable);

    /// <summary>
    /// Lists Gemini models live from the ListModels API. All fields come from
    /// the response; nothing is hardcoded locally.
    /// </summary>
    public static class GeminiModelCatalog
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd(EchoXIVClient.UserAgent);
            return http;
        }

        public static async Task<List<GeminiModelInfo>> ListModelsAsync(string apiKey, CancellationToken ct = default)
        {
            var result = new List<GeminiModelInfo>();
            if (string.IsNullOrWhiteSpace(apiKey))
                return result;

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://generativelanguage.googleapis.com/v1beta/models?key=" + Uri.EscapeDataString(apiKey));
                using var response = await SharedHttpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    return result;

                var body = await response.Content.ReadAsStringAsync(ct);
                var root = JObject.Parse(body);
                foreach (var item in root["models"] as JArray ?? new JArray())
                {
                    var name = item["name"]?.ToString() ?? "";
                    var id = name.StartsWith("models/") ? name["models/".Length..] : name;
                    var methods = (item["supportedGenerationMethods"] as JArray ?? new JArray())
                        .Select(m => m.ToString())
                        .ToArray();

                    result.Add(new GeminiModelInfo(
                        id,
                        item["displayName"]?.ToString() ?? id,
                        methods.Contains("generateContent")));
                }
            }
            catch
            {
                // Network or parse failure -> empty list.
            }
            return result;
        }
    }
}

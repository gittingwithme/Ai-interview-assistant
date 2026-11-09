using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GhostAI {
    public class ChatMessage {
        public string Role { get; set; } // "user" or "model"
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }

        public ChatMessage(string role, string content) {
            Role = role;
            Content = content;
            Timestamp = DateTime.Now;
        }
    }

    public class GeminiService {
        private readonly HttpClient _httpClient;
        private readonly JavaScriptSerializer _serializer;

        public GeminiService() {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            _httpClient = new HttpClient {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _serializer = new JavaScriptSerializer {
                MaxJsonLength = 50 * 1024 * 1024
            };
        }

        public async Task<string> SendMessageAsync(
            string apiKey,
            string model,
            string systemPrompt,
            string userMessage,
            List<ChatMessage> history = null) {

            if (string.IsNullOrWhiteSpace(apiKey)) {
                throw new InvalidOperationException("API key is not configured. Please open Settings and enter your Gemini API key.");
            }

            string cleanModel = string.IsNullOrWhiteSpace(model) ? "gemini-3.5-flash" : model.Trim();
            string url = string.Format("https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}", cleanModel, apiKey.Trim());

            var contents = new List<object>();

            if (history != null) {
                foreach (var msg in history) {
                    contents.Add(new Dictionary<string, object> {
                        { "role", msg.Role == "user" ? "user" : "model" },
                        { "parts", new object[] { new Dictionary<string, object> { { "text", msg.Content } } } }
                    });
                }
            }

            contents.Add(new Dictionary<string, object> {
                { "role", "user" },
                { "parts", new object[] { new Dictionary<string, object> { { "text", userMessage } } } }
            });

            var requestBody = new Dictionary<string, object> {
                { "contents", contents },
                { "generationConfig", new Dictionary<string, object> {
                    { "temperature", 0.7 },
                    { "maxOutputTokens", 2048 }
                }}
            };

            if (!string.IsNullOrWhiteSpace(systemPrompt)) {
                requestBody["systemInstruction"] = new Dictionary<string, object> {
                    { "parts", new object[] { new Dictionary<string, object> { { "text", systemPrompt.Trim() } } } }
                };
            }

            string jsonPayload = _serializer.Serialize(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(url, content);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) {
                string errorMsg = "API Error " + (int)response.StatusCode;
                try {
                    var errorObj = _serializer.Deserialize<Dictionary<string, object>>(responseString);
                    if (errorObj != null && errorObj.ContainsKey("error")) {
                        var errDict = errorObj["error"] as Dictionary<string, object>;
                        if (errDict != null && errDict.ContainsKey("message")) {
                            errorMsg = errDict["message"].ToString();
                        }
                    }
                } catch { }
                throw new Exception(errorMsg);
            }

            var root = _serializer.Deserialize<Dictionary<string, object>>(responseString);
            if (root != null && root.ContainsKey("candidates")) {
                var candidates = root["candidates"] as object[];
                if (candidates != null && candidates.Length > 0) {
                    var candidate = candidates[0] as Dictionary<string, object>;
                    if (candidate != null && candidate.ContainsKey("content")) {
                        var contentDict = candidate["content"] as Dictionary<string, object>;
                        if (contentDict != null && contentDict.ContainsKey("parts")) {
                            var parts = contentDict["parts"] as object[];
                            if (parts != null && parts.Length > 0) {
                                var part = parts[0] as Dictionary<string, object>;
                                if (part != null && part.ContainsKey("text")) {
                                    return part["text"].ToString();
                                }
                            }
                        }
                    }
                }
            }

            throw new Exception("Received empty response from Gemini model.");
        }

        public async Task<string> TranscribeAudioAsync(string apiKey, string model, byte[] wavAudioData) {
            if (string.IsNullOrWhiteSpace(apiKey)) {
                throw new InvalidOperationException("API key is not configured.");
            }

            string cleanModel = string.IsNullOrWhiteSpace(model) ? "gemini-3.5-flash" : model.Trim();
            string url = string.Format("https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}", cleanModel, apiKey.Trim());

            string base64Audio = Convert.ToBase64String(wavAudioData);

            var contents = new object[] {
                new Dictionary<string, object> {
                    { "role", "user" },
                    { "parts", new object[] {
                        new Dictionary<string, object> {
                            { "text", "Please accurately transcribe everything spoken in this audio. Output only the verbatim transcription text, without quotation marks, preamble, or commentary." }
                        },
                        new Dictionary<string, object> {
                            { "inlineData", new Dictionary<string, object> {
                                { "mimeType", "audio/wav" },
                                { "data", base64Audio }
                            }}
                        }
                    }}
                }
            };

            var requestBody = new Dictionary<string, object> {
                { "contents", contents },
                { "generationConfig", new Dictionary<string, object> {
                    { "temperature", 0.1 },
                    { "maxOutputTokens", 1024 }
                }}
            };

            string jsonPayload = _serializer.Serialize(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(url, content);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) {
                throw new Exception("Transcription error: " + response.StatusCode);
            }

            var root = _serializer.Deserialize<Dictionary<string, object>>(responseString);
            if (root != null && root.ContainsKey("candidates")) {
                var candidates = root["candidates"] as object[];
                if (candidates != null && candidates.Length > 0) {
                    var candidate = candidates[0] as Dictionary<string, object>;
                    if (candidate != null && candidate.ContainsKey("content")) {
                        var contentDict = candidate["content"] as Dictionary<string, object>;
                        if (contentDict != null && contentDict.ContainsKey("parts")) {
                            var parts = contentDict["parts"] as object[];
                            if (parts != null && parts.Length > 0) {
                                var part = parts[0] as Dictionary<string, object>;
                                if (part != null && part.ContainsKey("text")) {
                                    return part["text"].ToString().Trim();
                                }
                            }
                        }
                    }
                }
            }

            return "";
        }
    }
}

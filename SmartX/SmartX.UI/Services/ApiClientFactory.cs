
using System;
using System.Net.Http;

namespace SmartX.UI.Services
{
    public static class ApiClientFactory
    {
        public const string ApiBaseAddress = "https://localhost:7001/";

        private static readonly HttpClient _client = new()
        {
            BaseAddress = new Uri(ApiBaseAddress),
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static HttpClient Client => _client;
    }
}

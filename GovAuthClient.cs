using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GovAuthClient
{
    /// <summary>
    /// Cliente simples para a API Gov-Auth.
    /// Demonstra o fluxo de autenticação via browser-login e download de arquivos SIGEF.
    /// </summary>
    public class GovAuthApiClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public GovAuthApiClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Verifica status da sessão atual.
        /// </summary>
        public async Task<AuthStatus> GetAuthStatusAsync()
        {
            var response = await _client.GetAsync($"{_baseUrl}/v1/auth/status");
            var content = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Erro ao verificar status: {content}");
                return new AuthStatus { Authenticated = false };
            }

            return JsonSerializer.Deserialize<AuthStatus>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
        }

        /// <summary>
        /// Inicia o fluxo de autenticação via browser-login.
        /// Retorna a URL que deve ser aberta no navegador do usuário.
        /// </summary>
        public async Task<BrowserLoginResponse> StartBrowserLoginAsync()
        {
            var response = await _client.PostAsync($"{_baseUrl}/v1/auth/browser-login", null);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erro ao iniciar browser-login: {content}");
            }

            return JsonSerializer.Deserialize<BrowserLoginResponse>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
        }

        /// <summary>
        /// Abre a URL de login no navegador padrão do sistema.
        /// </summary>
        public void OpenBrowserForLogin(string loginUrl)
        {
            Console.WriteLine($"\nAbrindo navegador para autenticação...");
            Console.WriteLine($"URL: {loginUrl}\n");

            bool opened = false;

            // Tenta abrir com Chrome primeiro (recomendado para Gov.br)
            string[] chromePaths = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe")
            };

            foreach (var chromePath in chromePaths)
            {
                if (File.Exists(chromePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = chromePath,
                            Arguments = $"--new-window \"{loginUrl}\"",
                            UseShellExecute = false
                        });
                        Console.WriteLine("✓ Chrome aberto com sucesso!");
                        opened = true;
                        break;
                    }
                    catch { }
                }
            }

            // Fallback: tenta navegador padrão
            if (!opened)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
                    Console.WriteLine("✓ Navegador padrão aberto!");
                    opened = true;
                }
                catch { }
            }

            if (!opened)
            {
                Console.WriteLine($"\n>>> Por favor, copie e cole esta URL no seu navegador:");
                Console.WriteLine($"\n    {loginUrl}\n");
            }
        }

        /// <summary>
        /// Aguarda o usuário completar a autenticação no navegador.
        /// Verifica periodicamente o status da sessão.
        /// </summary>
        public async Task<bool> WaitForAuthenticationAsync(int timeoutSeconds = 300, int pollIntervalSeconds = 3)
        {
            Console.WriteLine("Aguardando autenticação no navegador...");
            Console.WriteLine("(Faça login no Gov.br com seu certificado digital)\n");

            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            while (DateTime.Now - startTime < timeout)
            {
                var status = await GetAuthStatusAsync();

                if (status.Authenticated)
                {
                    Console.WriteLine("✓ Autenticado com sucesso!");
                    return true;
                }

                Console.Write(".");
                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));
            }

            Console.WriteLine("\n✗ Timeout aguardando autenticação.");
            return false;
        }

        /// <summary>
        /// Faz download de todos os arquivos CSV de uma parcela em um ZIP.
        /// </summary>
        public async Task<byte[]> DownloadAllFilesAsync(string codigoParcela)
        {
            Console.WriteLine($"\nBaixando arquivos da parcela: {codigoParcela}");

            var response = await _client.GetAsync($"{_baseUrl}/v1/sigef/arquivo/todos/{codigoParcela}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao baixar arquivos: {error}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            Console.WriteLine($"✓ Download concluído: {bytes.Length} bytes");

            return bytes;
        }

        /// <summary>
        /// Faz download de um tipo específico de CSV.
        /// </summary>
        public async Task<byte[]> DownloadCsvAsync(string codigoParcela, string tipo)
        {
            // tipo: parcela, vertice, limite
            var response = await _client.GetAsync($"{_baseUrl}/v1/sigef/arquivo/csv/{codigoParcela}/{tipo}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao baixar CSV: {error}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Consulta parcelas por bounding box (não requer autenticação).
        /// </summary>
        public async Task<string> ConsultarPorBboxAsync(double minX, double minY, double maxX, double maxY)
        {
            var coords = $"{minX},{minY},{maxX},{maxY}";
            var response = await _client.GetAsync($"{_baseUrl}/v1/consultar/bbox/{coords}");
            return await response.Content.ReadAsStringAsync();
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }

    // DTOs
    public class AuthStatus
    {
        public bool Authenticated { get; set; }
        public string Message { get; set; }
        public SessionInfo Session { get; set; }
    }

    public class SessionInfo
    {
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string ExpiresAt { get; set; }
    }

    public class BrowserLoginResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("auth_token")]
        public string AuthToken { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("session_id")]
        public string SessionId { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("login_url")]
        public string LoginUrl { get; set; }
    }
}

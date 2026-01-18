using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace GovAuthClient
{
    /// <summary>
    /// Cliente de autenticação usando Playwright.
    /// Funciona igual à API Python - abre Chrome, captura cookies/localStorage/JWT automaticamente.
    /// </summary>
    public class PlaywrightAuthClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private IPlaywright _playwright;
        private IBrowser _browser;

        // URLs
        private const string SIGEF_URL = "https://sigef.incra.gov.br";
        private const string SIGEF_OAUTH_URL = "https://sigef.incra.gov.br/oauth2/authorization/govbr";
        private const string GOVBR_SSO_URL = "https://sso.acesso.gov.br";

        public PlaywrightAuthClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Autentica usando Playwright - igual à API Python.
        /// Abre Chrome do sistema, aguarda login, captura tudo automaticamente.
        /// </summary>
        public async Task<AuthResult> AuthenticateAsync(int timeoutSeconds = 300)
        {
            Console.WriteLine("\n🔐 Iniciando autenticação com Playwright...\n");

            // Inicializa Playwright
            _playwright = await Playwright.CreateAsync();

            // Usa Chrome do sistema (channel: "chrome") para acessar certificados
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "chrome",  // Usa Chrome instalado no sistema
                Headless = false,    // Precisa ser visível para seleção de certificado
                Args = new[] { "--disable-blink-features=AutomationControlled" }
            });

            Console.WriteLine("✓ Chrome aberto");

            // Cria contexto
            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });

            var page = await context.NewPageAsync();

            try
            {
                // Navega para SIGEF (que redireciona para Gov.br)
                Console.WriteLine("📡 Navegando para SIGEF...");
                await page.GotoAsync(SIGEF_URL, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60000
                });

                // Procura e clica no botão de login
                Console.WriteLine("🔍 Procurando botão de login...");
                var loginClicked = await TryClickLoginButton(page);

                if (!loginClicked)
                {
                    // Pode já estar na página de login ou já logado
                    Console.WriteLine("   (Botão não encontrado - verificando se já está logado)");
                }

                // Aguarda autenticação
                Console.WriteLine("\n⏳ Aguardando autenticação...");
                Console.WriteLine("   → Selecione seu certificado digital");
                Console.WriteLine("   → Complete o login no Gov.br\n");

                var authenticated = await WaitForAuthenticationComplete(page, context, timeoutSeconds);

                if (!authenticated)
                {
                    return new AuthResult { Success = false, Error = "Timeout aguardando autenticação" };
                }

                // Captura todos os dados
                Console.WriteLine("\n📦 Capturando dados de autenticação...");

                // 1. Captura cookies
                var cookies = await context.CookiesAsync();
                Console.WriteLine($"   ✓ {cookies.Count} cookies capturados");

                // 2. Captura localStorage
                var localStorage = await page.EvaluateAsync<Dictionary<string, string>>(@"() => {
                    const items = {};
                    for (let i = 0; i < localStorage.length; i++) {
                        const key = localStorage.key(i);
                        items[key] = localStorage.getItem(key);
                    }
                    return items;
                }");
                Console.WriteLine($"   ✓ {localStorage.Count} itens do localStorage capturados");

                // 3. Extrai JWT do localStorage
                var jwtData = ExtractJwtFromStorage(localStorage);
                if (jwtData != null)
                {
                    Console.WriteLine($"   ✓ JWT extraído (CPF: {jwtData.Cpf?.Substring(0, 3)}...)");
                }

                // 4. Salva storage_state (igual Python)
                var storageStatePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GovAuth", "storage_state.json"
                );
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(storageStatePath)!);
                await context.StorageStateAsync(new BrowserContextStorageStateOptions
                {
                    Path = storageStatePath
                });
                Console.WriteLine($"   ✓ Storage state salvo em: {storageStatePath}");

                // Envia para a API
                Console.WriteLine("\n📤 Enviando dados para a API...");
                var sendResult = await SendAuthDataToApi(cookies, localStorage, jwtData);

                if (sendResult)
                {
                    Console.WriteLine("\n✅ Autenticação concluída com sucesso!");
                    return new AuthResult
                    {
                        Success = true,
                        Cookies = cookies,
                        LocalStorage = localStorage,
                        JwtData = jwtData,
                        StorageStatePath = storageStatePath
                    };
                }
                else
                {
                    return new AuthResult { Success = false, Error = "Erro ao enviar dados para a API" };
                }
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, Error = ex.Message };
            }
            finally
            {
                await _browser.CloseAsync();
            }
        }

        private async Task<bool> TryClickLoginButton(IPage page)
        {
            string[] selectors = new[]
            {
                "button.sign-in",
                "button:has-text('Entrar')",
                "text=Entrar",
                "a[href*='oauth']",
                ".br-button.sign-in"
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var button = page.Locator(selector).First;
                    if (await button.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 2000 }))
                    {
                        await button.ClickAsync();
                        Console.WriteLine($"   ✓ Clicado: {selector}");
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private async Task<bool> WaitForAuthenticationComplete(IPage page, IBrowserContext context, int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            while (DateTime.Now - startTime < timeout)
            {
                // Verifica se está no SIGEF autenticado
                var currentUrl = page.Url;

                // Se está no SIGEF e não na página de OAuth, provavelmente está autenticado
                if (currentUrl.Contains("sigef.incra.gov.br") && !currentUrl.Contains("oauth"))
                {
                    // Verifica se tem botão "Sair" (indica que está logado)
                    try
                    {
                        var logoutButton = page.Locator("text=Sair").First;
                        if (await logoutButton.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 1000 }))
                        {
                            Console.WriteLine("\n✓ Login detectado no SIGEF!");
                            return true;
                        }
                    }
                    catch { }

                    // Ou verifica localStorage por JWT
                    var localStorage = await page.EvaluateAsync<Dictionary<string, string>>(@"() => {
                        const items = {};
                        for (let i = 0; i < localStorage.length; i++) {
                            const key = localStorage.key(i);
                            items[key] = localStorage.getItem(key);
                        }
                        return items;
                    }");

                    var jwt = ExtractJwtFromStorage(localStorage);
                    if (jwt != null)
                    {
                        Console.WriteLine("\n✓ JWT detectado!");
                        return true;
                    }
                }

                // Verifica cookies do SIGEF
                var cookies = await context.CookiesAsync(new[] { SIGEF_URL });
                var hasSessionCookie = cookies.Any(c => 
                    c.Name.Contains("SESSION", StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains("JSESSIONID", StringComparison.OrdinalIgnoreCase));

                if (hasSessionCookie && currentUrl.Contains("sigef.incra.gov.br"))
                {
                    Console.WriteLine("\n✓ Cookie de sessão detectado!");
                    return true;
                }

                Console.Write(".");
                await Task.Delay(2000);
            }

            return false;
        }

        private JwtPayload? ExtractJwtFromStorage(Dictionary<string, string> localStorage)
        {
            foreach (var kvp in localStorage)
            {
                var value = kvp.Value;
                if (string.IsNullOrEmpty(value)) continue;

                // JWT começa com eyJ
                if (value.StartsWith("eyJ"))
                {
                    return DecodeJwt(value);
                }

                // Tenta parsear como JSON e procurar tokens dentro
                try
                {
                    using var doc = JsonDocument.Parse(value);
                    var root = doc.RootElement;

                    foreach (var field in new[] { "access_token", "id_token", "token" })
                    {
                        if (root.TryGetProperty(field, out var tokenElement))
                        {
                            var token = tokenElement.GetString();
                            if (token?.StartsWith("eyJ") == true)
                            {
                                var jwt = DecodeJwt(token);
                                if (jwt != null)
                                {
                                    // Adiciona tokens brutos
                                    if (root.TryGetProperty("access_token", out var at))
                                        jwt.AccessToken = at.GetString();
                                    if (root.TryGetProperty("id_token", out var it))
                                        jwt.IdToken = it.GetString();
                                    return jwt;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private JwtPayload? DecodeJwt(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;

                // Decodifica payload (segunda parte)
                var payload = parts[1];
                // Adiciona padding
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                // Substitui caracteres URL-safe
                payload = payload.Replace('-', '+').Replace('_', '/');

                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new JwtPayload
                {
                    Cpf = root.TryGetProperty("sub", out var sub) ? sub.GetString() : 
                          root.TryGetProperty("cpf", out var cpf) ? cpf.GetString() : null,
                    Nome = root.TryGetProperty("name", out var name) ? name.GetString() :
                           root.TryGetProperty("nome", out var nome) ? nome.GetString() : null,
                    Email = root.TryGetProperty("email", out var email) ? email.GetString() : null,
                    Raw = json
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> SendAuthDataToApi(
            IReadOnlyList<BrowserContextCookiesResult> cookies,
            Dictionary<string, string> localStorage,
            JwtPayload? jwtData)
        {
            try
            {
                // Primeiro, inicia uma sessão de browser-login para obter o token
                var loginResponse = await _httpClient.PostAsync($"{_baseUrl}/v1/auth/browser-login", null);
                var loginContent = await loginResponse.Content.ReadAsStringAsync();

                if (!loginResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"   Erro ao iniciar browser-login: {loginContent}");
                    return false;
                }

                var loginData = JsonSerializer.Deserialize<BrowserLoginResponse>(loginContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Converte cookies para o formato da API
                var cookieList = new List<Dictionary<string, object>>();
                foreach (var cookie in cookies)
                {
                    cookieList.Add(new Dictionary<string, object>
                    {
                        ["name"] = cookie.Name,
                        ["value"] = cookie.Value,
                        ["domain"] = cookie.Domain,
                        ["path"] = cookie.Path,
                        ["secure"] = cookie.Secure,
                        ["httpOnly"] = cookie.HttpOnly
                    });
                }

                // Envia para o callback
                var callbackData = new
                {
                    auth_token = loginData.AuthToken,
                    govbr_cookies = cookieList,
                    sigef_cookies = cookieList,
                    jwt_payload = jwtData != null ? new
                    {
                        cpf = jwtData.Cpf,
                        nome = jwtData.Nome,
                        email = jwtData.Email,
                        access_token = jwtData.AccessToken,
                        id_token = jwtData.IdToken
                    } : null,
                    local_storage = localStorage
                };

                var json = JsonSerializer.Serialize(callbackData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/v1/auth/browser-callback", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("   ✓ Dados enviados com sucesso!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"   Erro: {responseContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Erro ao enviar: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica status da sessão atual.
        /// </summary>
        public async Task<AuthStatus> GetAuthStatusAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/auth/status");
            var content = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<AuthStatus>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AuthStatus { Authenticated = false };
        }

        /// <summary>
        /// Faz download de todos os arquivos CSV de uma parcela em um ZIP.
        /// </summary>
        public async Task<byte[]> DownloadAllFilesAsync(string codigoParcela)
        {
            Console.WriteLine($"\n📥 Baixando arquivos da parcela: {codigoParcela}");

            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/sigef/arquivo/todos/{codigoParcela}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao baixar arquivos: {error}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            Console.WriteLine($"   ✓ Download concluído: {bytes.Length:N0} bytes");

            return bytes;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _browser?.CloseAsync().GetAwaiter().GetResult();
            _playwright?.Dispose();
        }
    }

    // DTOs
    public class AuthResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public IReadOnlyList<BrowserContextCookiesResult>? Cookies { get; set; }
        public Dictionary<string, string>? LocalStorage { get; set; }
        public JwtPayload? JwtData { get; set; }
        public string? StorageStatePath { get; set; }
    }

    public class JwtPayload
    {
        public string? Cpf { get; set; }
        public string? Nome { get; set; }
        public string? Email { get; set; }
        public string? AccessToken { get; set; }
        public string? IdToken { get; set; }
        public string? Raw { get; set; }
    }
}

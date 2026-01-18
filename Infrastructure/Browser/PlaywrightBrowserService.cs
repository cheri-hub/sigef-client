using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SigefClient.Configuration;
using SigefClient.Domain.Entities;
using SigefClient.Domain.Interfaces;

namespace SigefClient.Infrastructure.Browser;

/// <summary>
/// Serviço de automação de browser usando Playwright
/// </summary>
public sealed class PlaywrightBrowserService : IBrowserAutomationService
{
    private readonly ILogger<PlaywrightBrowserService> _logger;
    private readonly SigefClientOptions _options;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _disposed;

    // Seletores CSS usados na automação
    private static class Selectors
    {
        // Botão "Entrar" no header do SIGEF (novo layout)
        public const string EntrarButton = "a:has-text('Entrar'), button:has-text('Entrar'), .br-sign-in, [href*='login'], [href*='entrar']";
        // Botão antigo (fallback)
        public const string LoginButton = ".acessar-govbr-sigef";
        public const string UserMenu = ".fa.fa-user";
    }

    // Chaves de storage usadas para detectar autenticação
    private static class StorageKeys
    {
        public const string SigefToken = "sigefToken";
        public const string LoginToken = "login_token";
        public const string Profile = "profile";
        public const string GovBrSession = "govbr.session";
    }

    public PlaywrightBrowserService(
        IOptions<SigefClientOptions> options,
        ILogger<PlaywrightBrowserService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsInitialized => _playwright is not null && _browser is not null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitialized)
        {
            _logger.LogDebug("Playwright já inicializado");
            return;
        }

        _logger.LogInformation("Inicializando Playwright...");

        _playwright = await Playwright.CreateAsync();
        _logger.LogDebug("Playwright criado");

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            Channel = "chrome",
            // IMPORTANTE: Desabilita detecção de automação para evitar captcha
            Args = new[] { "--disable-blink-features=AutomationControlled" }
        });
        _logger.LogDebug("Chrome lançado (anti-detecção habilitado)");

        // Cria contexto com viewport realista para evitar detecção
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
        });
        _page = await _context.NewPageAsync();
        _logger.LogInformation("Playwright inicializado com sucesso");
    }

    public async Task<OperationResult<AuthCallbackData>> AuthenticateAsync(
        string sigefUrl,
        string authToken,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsInitialized || _page is null)
        {
            return OperationResult<AuthCallbackData>.Fail("Playwright não inicializado. Chame InitializeAsync primeiro.");
        }

        try
        {
            _logger.LogInformation("Navegando para SIGEF: {Url}", sigefUrl);
            await NavigateToSigefAsync(sigefUrl);

            _logger.LogInformation("Clicando no botão de login...");
            await ClickLoginButtonAsync();

            _logger.LogInformation("Aguardando autenticação via Gov.br (timeout: {Timeout}s)...", timeout.TotalSeconds);
            _logger.LogInformation(">>> Por favor, complete a autenticação com certificado digital no navegador <<<");

            var authenticated = await WaitForAuthenticationAsync(timeout, cancellationToken);

            if (!authenticated)
            {
                return OperationResult<AuthCallbackData>.Fail("Timeout aguardando autenticação");
            }

            _logger.LogInformation("Autenticação detectada! Capturando dados...");
            var authData = await CaptureAuthenticationDataAsync(authToken);

            await SaveStorageStateAsync();

            // Fecha o browser após capturar os dados (não precisa mais)
            _logger.LogDebug("Fechando browser...");
            await CloseBrowserAsync();

            return OperationResult<AuthCallbackData>.Ok(authData);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Autenticação cancelada pelo usuário");
            return OperationResult<AuthCallbackData>.Fail("Autenticação cancelada");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro durante autenticação");
            return OperationResult<AuthCallbackData>.Fail($"Erro durante autenticação: {ex.Message}");
        }
    }

    private async Task NavigateToSigefAsync(string url)
    {
        await _page!.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60000
        });
        _logger.LogDebug("Navegação para SIGEF concluída");
    }

    private async Task ClickLoginButtonAsync()
    {
        try
        {
            // Tenta primeiro o botão "Entrar" do novo layout
            var entrarButton = await _page!.QuerySelectorAsync(Selectors.EntrarButton);
            
            if (entrarButton is not null)
            {
                _logger.LogDebug("Botão 'Entrar' encontrado no header");
                await entrarButton.ClickAsync();
                _logger.LogDebug("Botão 'Entrar' clicado");
                await Task.Delay(2000);
                return;
            }

            // Fallback: tenta o seletor antigo
            _logger.LogDebug("Tentando seletor alternativo...");
            await _page!.WaitForSelectorAsync(Selectors.LoginButton, new PageWaitForSelectorOptions
            {
                Timeout = 30000
            });

            await _page.ClickAsync(Selectors.LoginButton);
            _logger.LogDebug("Botão de login clicado");

            await Task.Delay(2000);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError("Botão de login não encontrado: {Selector}", Selectors.LoginButton);
            throw new InvalidOperationException($"Botão de login não encontrado: {Selectors.LoginButton}", ex);
        }
    }

    private async Task<bool> WaitForAuthenticationAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var pollInterval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsAuthenticatedAsync())
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return false;
    }

    private async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            var currentUrl = _page!.Url;
            
            // Só considera autenticado se voltou para o SIGEF (não está mais no Gov.br)
            if (!currentUrl.Contains("sigef.incra.gov.br", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Ainda não retornou ao SIGEF. URL atual: {Url}", currentUrl);
                return false;
            }

            // Ignora páginas de callback OAuth ou authorize
            if (currentUrl.Contains("oauth", StringComparison.OrdinalIgnoreCase) ||
                currentUrl.Contains("callback", StringComparison.OrdinalIgnoreCase) ||
                currentUrl.Contains("authorize", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Página de callback OAuth, aguardando...");
                return false;
            }

            // Aguarda a página carregar completamente
            try
            {
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 5000 });
            }
            catch
            {
                // Ignora timeout
            }

            // Tenta fechar qualquer modal que esteja aberto
            await TryCloseModalAsync();

            // VERIFICAÇÃO 1: Procura "Olá, " no header (indica usuário logado)
            var olaVisible = await _page.IsVisibleAsync("text=/Olá,/i");
            if (olaVisible)
            {
                _logger.LogDebug("Texto 'Olá' detectado - autenticado!");
                return true;
            }

            // VERIFICAÇÃO 2: O botão "Entrar" NÃO deve estar visível
            var entrarButtonVisible = await _page.IsVisibleAsync(Selectors.EntrarButton);
            if (entrarButtonVisible)
            {
                _logger.LogDebug("Botão 'Entrar' ainda visível - não autenticado");
                return false;
            }

            // VERIFICAÇÃO 3: Botão "Sair" visível (indica logado)
            var logoutVisible = await _page.IsVisibleAsync("text=Sair");
            if (logoutVisible)
            {
                _logger.LogDebug("Botão 'Sair' detectado - autenticado!");
                return true;
            }

            // VERIFICAÇÃO 4: Menu de usuário visível
            var userMenuVisible = await _page.IsVisibleAsync(Selectors.UserMenu);
            if (userMenuVisible)
            {
                _logger.LogDebug("Menu de usuário detectado no SIGEF - autenticado!");
                return true;
            }

            // VERIFICAÇÃO 5: Elementos que só aparecem quando logado
            var loggedInElements = await _page.IsVisibleAsync(
                ".user-name, .nome-usuario, .br-sign-in-out, [class*='logged'], .notificacoes");
            if (loggedInElements)
            {
                _logger.LogDebug("Elemento de usuário logado detectado!");
                return true;
            }

            _logger.LogDebug("Nenhum indicador de autenticação encontrado");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Erro ao verificar autenticação: {Error}", ex.Message);
            return false;
        }
    }

    private async Task TryCloseModalAsync()
    {
        try
        {
            // Tenta fechar modais comuns
            var closeButtons = new[]
            {
                "button:has-text('Fechar')",
                "button:has-text('OK')",
                "button:has-text('Entendi')",
                ".modal button.close",
                "[data-dismiss='modal']",
                ".btn-close"
            };

            foreach (var selector in closeButtons)
            {
                var button = await _page!.QuerySelectorAsync(selector);
                if (button is not null && await button.IsVisibleAsync())
                {
                    _logger.LogDebug("Fechando modal com: {Selector}", selector);
                    await button.ClickAsync();
                    await Task.Delay(500);
                    break;
                }
            }
        }
        catch
        {
            // Ignora erros ao fechar modal
        }
    }

    private async Task<Dictionary<string, string>> GetLocalStorageAsync()
    {
        var result = new Dictionary<string, string>();

        try
        {
            var keys = await _page!.EvaluateAsync<string[]>("() => Object.keys(localStorage)");

            foreach (var key in keys ?? Array.Empty<string>())
            {
                var value = await _page.EvaluateAsync<string>($"localStorage.getItem('{key}')");
                if (value is not null)
                {
                    result[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao obter localStorage: {Error}", ex.Message);
        }

        return result;
    }

    private async Task<AuthCallbackData> CaptureAuthenticationDataAsync(string authToken)
    {
        var allCookies = await _context!.CookiesAsync();
        
        // Separa cookies do Gov.br e do SIGEF
        var govBrCookies = allCookies
            .Where(c => c.Domain.Contains("gov.br", StringComparison.OrdinalIgnoreCase))
            .Select(ToCookieData)
            .ToList();
        
        var sigefCookies = allCookies
            .Where(c => c.Domain.Contains("sigef", StringComparison.OrdinalIgnoreCase) || 
                        c.Domain.Contains("incra", StringComparison.OrdinalIgnoreCase))
            .Select(ToCookieData)
            .ToList();

        // Tenta extrair JWT do localStorage
        var localStorage = await GetLocalStorageAsync();
        var jwtPayload = ExtractJwtPayload(localStorage);

        _logger.LogInformation("Dados capturados - Cookies Gov.br: {GovBrCount}, SIGEF: {SigefCount}, JWT: {HasJwt}",
            govBrCookies.Count, sigefCookies.Count, jwtPayload is not null);

        return new AuthCallbackData
        {
            AuthToken = authToken,
            GovBrCookies = govBrCookies,
            SigefCookies = sigefCookies.Count > 0 ? sigefCookies : null,
            JwtPayload = jwtPayload
        };
    }

    private static CookieData ToCookieData(BrowserContextCookiesResult cookie) => new()
    {
        Name = cookie.Name,
        Value = cookie.Value,
        Domain = cookie.Domain,
        Path = cookie.Path,
        Secure = cookie.Secure,
        HttpOnly = cookie.HttpOnly
    };

    private async Task<Dictionary<string, object>> CaptureCookiesAsync()
    {
        var result = new Dictionary<string, object>();

        var cookies = await _context!.CookiesAsync();

        foreach (var cookie in cookies)
        {
            result[cookie.Name] = new Dictionary<string, object>
            {
                ["value"] = cookie.Value,
                ["domain"] = cookie.Domain,
                ["path"] = cookie.Path,
                ["secure"] = cookie.Secure,
                ["httpOnly"] = cookie.HttpOnly,
                ["sameSite"] = cookie.SameSite.ToString()
            };
        }

        return result;
    }

    private static Dictionary<string, object>? ExtractJwtPayload(Dictionary<string, string> localStorage)
    {
        // Procura por tokens conhecidos em ordem de preferência
        string[] tokenKeys = [StorageKeys.SigefToken, StorageKeys.LoginToken, "token", "jwt", "access_token"];

        string? token = null;
        foreach (var key in tokenKeys)
        {
            if (localStorage.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                token = value;
                break;
            }
        }

        // Procura por qualquer chave que contenha "token"
        if (token is null)
        {
            var tokenEntry = localStorage.FirstOrDefault(kv =>
                kv.Key.Contains("token", StringComparison.OrdinalIgnoreCase));
            token = tokenEntry.Value;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // Tenta decodificar o JWT para extrair o payload
        try
        {
            var parts = token.Split('.');
            if (parts.Length >= 2)
            {
                // O payload é a segunda parte do JWT
                var payload = parts[1];
                // Adiciona padding se necessário
                var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var decoded = Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
                var json = System.Text.Encoding.UTF8.GetString(decoded);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }
        }
        catch
        {
            // Se falhar ao decodificar, retorna o token como está
        }

        return new Dictionary<string, object> { ["token"] = token };
    }

    private async Task SaveStorageStateAsync()
    {
        try
        {
            var path = _options.GetStorageStatePath();
            await _context!.StorageStateAsync(new BrowserContextStorageStateOptions
            {
                Path = path
            });
            _logger.LogInformation("Estado do storage salvo em: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erro ao salvar storage state: {Error}", ex.Message);
        }
    }

    private async Task CloseBrowserAsync()
    {
        try
        {
            if (_page is not null)
            {
                await _page.CloseAsync();
                _page = null;
            }

            if (_context is not null)
            {
                await _context.CloseAsync();
                _context = null;
            }

            if (_browser is not null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Erro ao fechar browser: {Error}", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _logger.LogDebug("Liberando recursos do Playwright...");

        if (_page is not null)
        {
            await _page.CloseAsync();
        }

        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();

        _disposed = true;
        _logger.LogDebug("Recursos do Playwright liberados");
    }
}

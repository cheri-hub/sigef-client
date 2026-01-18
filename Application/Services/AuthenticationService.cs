using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SigefClient.Configuration;
using SigefClient.Domain.Entities;
using SigefClient.Domain.Interfaces;

namespace SigefClient.Application.Services;

/// <summary>
/// Serviço principal de autenticação - orquestra API client e automação de browser
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly ISigefApiClient _apiClient;
    private readonly IBrowserAutomationService _browserService;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly SigefClientOptions _options;

    public AuthenticationService(
        ISigefApiClient apiClient,
        IBrowserAutomationService browserService,
        IOptions<SigefClientOptions> options,
        ILogger<AuthenticationService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _browserService = browserService ?? throw new ArgumentNullException(nameof(browserService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationResult<bool>> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Verificando status de autenticação...");

        var statusResult = await _apiClient.GetAuthStatusAsync(cancellationToken);

        if (!statusResult.Success)
        {
            _logger.LogWarning("Não foi possível verificar status: {Error}", statusResult.ErrorMessage);
            // Continua para tentar autenticar mesmo assim
        }
        else if (statusResult.Data?.IsFullyAuthenticated == true)
        {
            var session = statusResult.Data.Session!;
            _logger.LogInformation("Já autenticado como: {Nome} (CPF: {Cpf})",
                session.Nome ?? "N/A", session.Cpf ?? "N/A");
            _logger.LogInformation("Sessão: {SessionId}, Gov.br: {GovBr}, SIGEF: {Sigef}",
                session.SessionId[..8] + "...",
                session.IsGovBrAuthenticated ? "✓" : "✗",
                session.IsSigefAuthenticated ? "✓" : "✗");
            return OperationResult<bool>.Ok(true);
        }
        else if (statusResult.Data?.Authenticated == true)
        {
            // Sessão existe mas não está completa (falta SIGEF ou dados do usuário)
            _logger.LogWarning("Sessão parcial detectada. Necessário re-autenticar.");
        }

        _logger.LogInformation("Não autenticado. Iniciando processo de autenticação...");
        return await PerformAuthenticationAsync(cancellationToken);
    }

    public async Task<OperationResult<bool>> ForceReauthenticateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Forçando nova autenticação...");
        return await PerformAuthenticationAsync(cancellationToken);
    }

    public async Task<OperationResult<AuthStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await _apiClient.GetAuthStatusAsync(cancellationToken);
    }

    public async Task<OperationResult<byte[]>> DownloadAllFilesAsync(string codigoImovel, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Baixando todos os arquivos do imóvel: {Codigo}", codigoImovel);
        return await _apiClient.DownloadAllFilesAsync(codigoImovel, cancellationToken);
    }

    public async Task<OperationResult<byte[]>> DownloadCsvAsync(string codigoImovel, string tipo, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Baixando CSV {Tipo} do imóvel {Codigo}", tipo, codigoImovel);
        return await _apiClient.DownloadCsvAsync(codigoImovel, tipo, cancellationToken);
    }

    public async Task<OperationResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Realizando logout...");
        var result = await _apiClient.LogoutAsync(cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Falha no logout: {Error}", result.ErrorMessage);
            return OperationResult<bool>.Fail(result.ErrorMessage ?? "Falha no logout");
        }

        _logger.LogInformation("Logout realizado: {Message}", result.Data?.Message);
        return OperationResult<bool>.Ok(true);
    }

    private async Task<OperationResult<bool>> PerformAuthenticationAsync(CancellationToken cancellationToken)
    {
        // 1. Inicia browser-login na API
        var loginResult = await StartBrowserLoginAsync(cancellationToken);
        if (!loginResult.Success || loginResult.Data is null)
        {
            return OperationResult<bool>.Fail(loginResult.ErrorMessage ?? "Falha ao iniciar browser-login");
        }

        var authToken = loginResult.Data.AuthToken;
        _logger.LogDebug("Token de autenticação: {AuthToken}", authToken[..Math.Min(20, authToken.Length)] + "...");

        // 2. Inicializa automação de browser
        var initResult = await InitializeBrowserAsync(cancellationToken);
        if (!initResult.Success)
        {
            return initResult;
        }

        // 3. Executa autenticação no browser
        var authResult = await ExecuteBrowserAuthenticationAsync(authToken, cancellationToken);
        if (!authResult.Success || authResult.Data is null)
        {
            return OperationResult<bool>.Fail(authResult.ErrorMessage ?? "Falha na autenticação");
        }

        // 4. Envia dados para a API
        var callbackResult = await SendAuthCallbackAsync(authResult.Data, cancellationToken);
        if (!callbackResult.Success)
        {
            return OperationResult<bool>.Fail(callbackResult.ErrorMessage ?? "Falha ao enviar callback");
        }

        _logger.LogInformation("Autenticação concluída com sucesso!");
        return OperationResult<bool>.Ok(true);
    }

    private async Task<OperationResult<BrowserLoginResponse>> StartBrowserLoginAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Iniciando browser-login na API...");
        var result = await _apiClient.StartBrowserLoginAsync(cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Falha ao iniciar browser-login: {Error}", result.ErrorMessage);
        }
        else
        {
            _logger.LogDebug("Browser-login iniciado. URL: {Url}", result.Data?.LoginUrl);
        }

        return result;
    }

    private async Task<OperationResult<bool>> InitializeBrowserAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _browserService.InitializeAsync(cancellationToken);
            return OperationResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao inicializar browser");
            return OperationResult<bool>.Fail($"Falha ao inicializar browser: {ex.Message}");
        }
    }

    private async Task<OperationResult<AuthCallbackData>> ExecuteBrowserAuthenticationAsync(
        string authToken,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.AuthTimeoutSeconds);
        return await _browserService.AuthenticateAsync(_options.SigefUrl, authToken, timeout, cancellationToken);
    }

    private async Task<OperationResult<bool>> SendAuthCallbackAsync(
        AuthCallbackData data,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Enviando dados de autenticação para a API...");
        var result = await _apiClient.SendAuthCallbackAsync(data, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Falha ao enviar callback: {Error}", result.ErrorMessage);
            return OperationResult<bool>.Fail(result.ErrorMessage ?? "Falha ao enviar callback");
        }

        _logger.LogInformation("Callback enviado. Status: {Status}, Mensagem: {Message}",
            result.Data?.Status, result.Data?.Message);

        return OperationResult<bool>.Ok(true);
    }
}

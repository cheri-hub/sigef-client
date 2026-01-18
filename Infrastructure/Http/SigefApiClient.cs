using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SigefClient.Configuration;
using SigefClient.Domain.Entities;
using SigefClient.Domain.Interfaces;

namespace SigefClient.Infrastructure.Http;

/// <summary>
/// Cliente HTTP para a API GovAuth/SIGEF
/// </summary>
public sealed class SigefApiClient : ISigefApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SigefApiClient> _logger;
    private readonly SigefClientOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SigefApiClient(
        HttpClient httpClient,
        IOptions<SigefClientOptions> options,
        ILogger<SigefApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // HttpClient já configurado via DI
    }

    public async Task<OperationResult<AuthStatus>> GetAuthStatusAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "v1/auth/status";
        _logger.LogDebug("Verificando status de autenticação em {Endpoint}", endpoint);

        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            return await HandleResponseAsync<AuthStatus>(response, "verificar status de autenticação", cancellationToken);
        }
        catch (Exception ex)
        {
            return HandleException<AuthStatus>(ex, "verificar status de autenticação");
        }
    }

    public async Task<OperationResult<BrowserLoginResponse>> StartBrowserLoginAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "v1/auth/browser-login";
        _logger.LogDebug("Iniciando browser-login em {Endpoint}", endpoint);

        try
        {
            var response = await _httpClient.PostAsync(endpoint, null, cancellationToken);
            return await HandleResponseAsync<BrowserLoginResponse>(response, "iniciar browser-login", cancellationToken);
        }
        catch (Exception ex)
        {
            return HandleException<BrowserLoginResponse>(ex, "iniciar browser-login");
        }
    }

    public async Task<OperationResult<CallbackResponse>> SendAuthCallbackAsync(AuthCallbackData data, CancellationToken cancellationToken = default)
    {
        const string endpoint = "v1/auth/browser-callback";
        _logger.LogDebug("Enviando callback de autenticação para {Endpoint}", endpoint);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, data, cancellationToken);
            return await HandleResponseAsync<CallbackResponse>(response, "enviar callback de autenticação", cancellationToken);
        }
        catch (Exception ex)
        {
            return HandleException<CallbackResponse>(ex, "enviar callback de autenticação");
        }
    }

    public async Task<OperationResult<byte[]>> DownloadAllFilesAsync(string codigoImovel, CancellationToken cancellationToken = default)
    {
        var endpoint = $"v1/sigef/arquivo/todos/{codigoImovel}";
        _logger.LogDebug("Baixando todos os arquivos do imóvel {Codigo} (ZIP)", codigoImovel);

        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            return await HandleBinaryResponseAsync(response, $"baixar arquivos do imóvel {codigoImovel}", cancellationToken);
        }
        catch (Exception ex)
        {
            return HandleException<byte[]>(ex, $"baixar arquivos do imóvel {codigoImovel}");
        }
    }

    public async Task<OperationResult<byte[]>> DownloadCsvAsync(string codigoImovel, string tipo, CancellationToken cancellationToken = default)
    {
        var endpoint = $"v1/sigef/arquivo/csv/{codigoImovel}/{tipo}";
        _logger.LogDebug("Baixando CSV {Tipo} do imóvel {Codigo}", tipo, codigoImovel);

        try
        {
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            return await HandleBinaryResponseAsync(response, $"baixar CSV {tipo} do imóvel {codigoImovel}", cancellationToken);
        }
        catch (Exception ex)
        {
            return HandleException<byte[]>(ex, $"baixar CSV {tipo}");
        }
    }

    public async Task<OperationResult<LogoutResponse>> LogoutAsync(CancellationToken cancellationToken = default)
    {
        const string endpoint = "v1/auth/logout";
        _logger.LogDebug("Realizando logout em {Endpoint}", endpoint);

        try
        {
            var response = await _httpClient.PostAsync(endpoint, null, cancellationToken);
            return await HandleResponseAsync<LogoutResponse>(response, "realizar logout", cancellationToken);
        }
        catch (Exception ex)
        {
            return HandleException<LogoutResponse>(ex, "realizar logout");
        }
    }

    private async Task<OperationResult<byte[]>> HandleBinaryResponseAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Falha ao {Operation}. Status: {Status}, Erro: {Error}",
                operation, response.StatusCode, errorContent);
            return OperationResult<byte[]>.Fail($"Falha ao {operation}: {response.StatusCode} - {errorContent}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        _logger.LogInformation("Download concluído: {Size} bytes", bytes.Length);
        return OperationResult<byte[]>.Ok(bytes);
    }

    private async Task<OperationResult<T>> HandleResponseAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Falha ao {Operation}. Status: {Status}, Resposta: {Content}",
                operation, response.StatusCode, content);
            return OperationResult<T>.Fail($"Falha ao {operation}: {response.StatusCode} - {content}");
        }

        _logger.LogDebug("Resposta recebida para {Operation}: {Content}", operation, content);

        try
        {
            var result = JsonSerializer.Deserialize<T>(content, JsonOptions);
            if (result is null)
            {
                _logger.LogError("Falha ao deserializar resposta para {Type}", typeof(T).Name);
                return OperationResult<T>.Fail($"Falha ao deserializar resposta para {typeof(T).Name}");
            }
            return OperationResult<T>.Ok(result);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Erro ao deserializar resposta: {Content}", content);
            return OperationResult<T>.Fail($"Erro ao deserializar resposta: {ex.Message}");
        }
    }

    private OperationResult<T> HandleException<T>(Exception ex, string operation)
    {
        _logger.LogError(ex, "Erro ao {Operation}", operation);
        return OperationResult<T>.Fail($"Erro ao {operation}: {ex.Message}");
    }
}

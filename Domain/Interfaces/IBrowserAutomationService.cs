using SigefClient.Domain.Entities;

namespace SigefClient.Domain.Interfaces;

/// <summary>
/// Interface para automação de browser (Playwright)
/// </summary>
public interface IBrowserAutomationService : IAsyncDisposable
{
    /// <summary>
    /// Inicializa o serviço de automação
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa o fluxo de autenticação no SIGEF
    /// </summary>
    /// <param name="sigefUrl">URL do SIGEF</param>
    /// <param name="authToken">Token de autenticação obtido da API</param>
    /// <param name="timeout">Timeout para aguardar autenticação</param>
    /// <param name="cancellationToken">Token de cancelamento</param>
    /// <returns>Dados de autenticação capturados</returns>
    Task<OperationResult<AuthCallbackData>> AuthenticateAsync(
        string sigefUrl,
        string authToken,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica se o serviço foi inicializado
    /// </summary>
    bool IsInitialized { get; }
}

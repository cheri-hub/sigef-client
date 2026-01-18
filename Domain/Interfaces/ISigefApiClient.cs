using SigefClient.Domain.Entities;

namespace SigefClient.Domain.Interfaces;

/// <summary>
/// Interface para o cliente da API GovAuth/SIGEF
/// </summary>
public interface ISigefApiClient
{
    /// <summary>
    /// Verifica o status de autenticação atual
    /// </summary>
    Task<OperationResult<AuthStatus>> GetAuthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia o processo de login via browser
    /// </summary>
    Task<OperationResult<BrowserLoginResponse>> StartBrowserLoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia os dados de autenticação capturados do browser
    /// </summary>
    Task<OperationResult<CallbackResponse>> SendAuthCallbackAsync(AuthCallbackData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baixa todos os arquivos de uma parcela como ZIP
    /// </summary>
    Task<OperationResult<byte[]>> DownloadAllFilesAsync(string codigoImovel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baixa um CSV específico (parcela, vertice ou limite)
    /// </summary>
    Task<OperationResult<byte[]>> DownloadCsvAsync(string codigoImovel, string tipo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Realiza logout da sessão atual
    /// </summary>
    Task<OperationResult<LogoutResponse>> LogoutAsync(CancellationToken cancellationToken = default);
}

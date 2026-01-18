using SigefClient.Domain.Entities;

namespace SigefClient.Domain.Interfaces;

/// <summary>
/// Interface principal para o serviço de autenticação e download do SIGEF
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Verifica se está autenticado e tenta autenticar se necessário
    /// </summary>
    /// <returns>True se autenticado com sucesso</returns>
    Task<OperationResult<bool>> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Força uma nova autenticação mesmo se já estiver autenticado
    /// </summary>
    Task<OperationResult<bool>> ForceReauthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém o status atual de autenticação
    /// </summary>
    Task<OperationResult<AuthStatus>> GetStatusAsync(CancellationToken cancellationToken = default);

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
    Task<OperationResult<bool>> LogoutAsync(CancellationToken cancellationToken = default);
}

namespace SigefClient.Configuration;

/// <summary>
/// Configurações do cliente SIGEF - usa Options Pattern para configuração segura
/// </summary>
public class SigefClientOptions
{
    public const string SectionName = "SigefClient";

    /// <summary>
    /// URL base da API (ex: https://govauth.cherihub.cloud/api)
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Chave de API para autenticação - DEVE ser configurada via User Secrets ou variável de ambiente
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Timeout geral em segundos
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Timeout específico para autenticação em segundos
    /// </summary>
    public int AuthTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Intervalo de polling para verificar autenticação em segundos
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>
    /// URL do SIGEF
    /// </summary>
    public string SigefUrl { get; set; } = "https://sigef.incra.gov.br";

    /// <summary>
    /// Caminho para salvar o storage_state.json - se vazio, usa diretório atual
    /// </summary>
    public string StorageStatePath { get; set; } = string.Empty;

    /// <summary>
    /// Retorna o caminho efetivo do storage_state.json
    /// </summary>
    public string GetStorageStatePath() =>
        string.IsNullOrWhiteSpace(StorageStatePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "storage_state.json")
            : StorageStatePath;
}

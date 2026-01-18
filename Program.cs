using System;
using System.IO;
using System.Threading.Tasks;

namespace GovAuthClient
{
    class Program
    {
        // Configuração
        private const string API_BASE_URL = "https://govauth.cherihub.cloud/api";
        private const string API_KEY = "554a8a59e662237b25231bba27e659a0dae67d8224e66ccb34ee9381e13aee5f";

        static async Task Main(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     Gov-Auth API - Cliente C# com Playwright              ║");
            Console.WriteLine("║     Autenticação automática igual à API Python            ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════╝\n");

            using var client = new PlaywrightAuthClient(API_BASE_URL, API_KEY);

            try
            {
                // 1. Verificar status atual
                Console.WriteLine("[1] Verificando status da sessão atual...");
                var status = await client.GetAuthStatusAsync();
                Console.WriteLine($"    Autenticado: {status.Authenticated}");
                Console.WriteLine($"    Mensagem: {status.Message}\n");

                // 2. Se não autenticado, usar Playwright para autenticar
                if (!status.Authenticated)
                {
                    Console.WriteLine("[2] Iniciando autenticação via Playwright...");
                    Console.WriteLine("    (O Chrome será aberto automaticamente)\n");

                    var result = await client.AuthenticateAsync(timeoutSeconds: 300);

                    if (!result.Success)
                    {
                        Console.WriteLine($"\n❌ Falha na autenticação: {result.Error}");
                        Console.WriteLine("\nPressione qualquer tecla para sair...");
                        Console.ReadKey();
                        return;
                    }

                    // Mostra info
                    if (result.JwtData != null)
                    {
                        Console.WriteLine($"\n👤 Usuário: {result.JwtData.Nome}");
                        Console.WriteLine($"📧 Email: {result.JwtData.Email}");
                    }
                }

                // 3. Testar download
                Console.WriteLine("\n[3] Testando download de arquivos do SIGEF...\n");

                // Código de exemplo - substitua por um código real de parcela
                var codigoParcela = "f7fd7a57-4858-4453-b132-74e74dee2101";

                Console.Write($"    Digite o código da parcela [{codigoParcela}]: ");
                var input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    codigoParcela = input.Trim();
                }

                try
                {
                    var zipBytes = await client.DownloadAllFilesAsync(codigoParcela);

                    // Salvar arquivo
                    var fileName = $"parcela_{codigoParcela.Substring(0, 8)}.zip";
                    await File.WriteAllBytesAsync(fileName, zipBytes);
                    Console.WriteLine($"\n    💾 Arquivo salvo: {Path.GetFullPath(fileName)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n    ❌ Erro no download: {ex.Message}");
                }

                Console.WriteLine("\n╔═══════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                    Teste Concluído!                       ║");
                Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Erro: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nPressione qualquer tecla para sair...");
            Console.ReadKey();
        }
    }
}

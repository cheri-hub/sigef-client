using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SigefClient.Application.Services;
using SigefClient.Configuration;
using SigefClient.Domain.Entities;
using SigefClient.Domain.Interfaces;
using SigefClient.Infrastructure.Browser;
using SigefClient.Infrastructure.Http;

namespace SigefClient;

/// <summary>
/// Ponto de entrada da aplicação - demonstração do cliente SIGEF
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var forceReauth = args.Contains("--force") || args.Contains("-f");
        var doLogout = args.Contains("--logout") || args.Contains("-l");
        
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var host = CreateHostBuilder(args).Build();
        var logger = host.Services.GetRequiredService<ILogger<object>>();

        try
        {
            logger.LogInformation("=== Cliente SIGEF v1.0.0 ===");
            logger.LogInformation("Uso: dotnet run [--force|-f] [--logout|-l]");
            logger.LogInformation("Pressione Ctrl+C para cancelar a qualquer momento");
            Console.WriteLine();

            await RunDemoAsync(host.Services, forceReauth, doLogout, cts.Token);

            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Operação cancelada pelo usuário");
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Erro fatal na aplicação");
            return 1;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                    .AddUserSecrets<SigefClientOptions>(optional: true)
                    .AddEnvironmentVariables("SIGEF_")
                    .AddCommandLine(args);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddConfiguration(context.Configuration.GetSection("Logging"));
            })
            .ConfigureServices((context, services) =>
            {
                // Registra Options
                var options = new SigefClientOptions();
                context.Configuration.GetSection(SigefClientOptions.SectionName).Bind(options);
                
                services.Configure<SigefClientOptions>(
                    context.Configuration.GetSection(SigefClientOptions.SectionName));

                // Registra HttpClient com typed client e configuração
                services.AddHttpClient<ISigefApiClient, SigefApiClient>(client =>
                {
                    client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
                    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                    client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                    client.DefaultRequestHeaders.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                });

                // Registra serviços
                services.AddTransient<IBrowserAutomationService, PlaywrightBrowserService>();
                services.AddTransient<IAuthenticationService, AuthenticationService>();
            });
    }

    private static async Task RunDemoAsync(IServiceProvider services, bool forceReauth, bool doLogout, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILogger<object>>();
        var authService = services.GetRequiredService<IAuthenticationService>();

        // Usar 'using' para garantir que o browser seja fechado
        await using var browserService = services.GetRequiredService<IBrowserAutomationService>();

        // Se pediu apenas logout, faz logout e sai
        if (doLogout && !forceReauth)
        {
            logger.LogInformation("Fazendo logout da sessão atual...");
            var logoutResult = await authService.LogoutAsync(cancellationToken);
            if (logoutResult.Success)
            {
                Console.WriteLine();
                Console.WriteLine("✅ Logout realizado com sucesso!");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"❌ Falha no logout: {logoutResult.ErrorMessage}");
            }
            return; // Sai sem tentar autenticar
        }

        // Se pediu logout + force, faz logout primeiro e depois autentica
        if (doLogout && forceReauth)
        {
            logger.LogInformation("Fazendo logout antes de re-autenticar...");
            await authService.LogoutAsync(cancellationToken);
        }

        // 1. Garante autenticação
        logger.LogInformation("Passo 1: Verificando/realizando autenticação...");
        
        OperationResult<bool> authResult;
        if (forceReauth)
        {
            logger.LogInformation("Forçando nova autenticação (--force)...");
            authResult = await authService.ForceReauthenticateAsync(cancellationToken);
        }
        else
        {
            authResult = await authService.EnsureAuthenticatedAsync(cancellationToken);
        }

        if (!authResult.Success)
        {
            logger.LogError("Falha na autenticação: {Error}", authResult.ErrorMessage);
            return;
        }

        // Pequeno delay para garantir que logs assíncronos sejam escritos
        await Task.Delay(100, cancellationToken);
        
        Console.WriteLine();
        Console.WriteLine("✅ Autenticação OK!");
        Console.WriteLine();

        // 2. Pedir código do imóvel ao usuário
        Console.WriteLine("Digite o código do imóvel (ex: f7fd7a57-4858-4453-b132-74e74dee2101):");
        Console.Write("> ");
        var codigoImovel = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(codigoImovel))
        {
            Console.WriteLine("Código não informado. Usando exemplo.");
            codigoImovel = "f7fd7a57-4858-4453-b132-74e74dee2101";
        }

        Console.WriteLine();

        // 3. Baixa arquivos do imóvel
        await DemoDownloadFilesAsync(authService, logger, codigoImovel, cancellationToken);
    }

    private static async Task DemoDownloadFilesAsync(
        IAuthenticationService authService,
        ILogger logger,
        string codigoImovel,
        CancellationToken cancellationToken)
    {
        // Opção 1: Baixar todos os arquivos como ZIP
        logger.LogInformation("Passo 2: Baixando todos os arquivos do imóvel {Codigo} (ZIP)...", codigoImovel);

        var zipResult = await authService.DownloadAllFilesAsync(codigoImovel, cancellationToken);

        if (!zipResult.Success)
        {
            logger.LogError("Falha ao baixar arquivos: {Error}", zipResult.ErrorMessage);
            return;
        }

        var zipPath = Path.Combine(Directory.GetCurrentDirectory(), $"{codigoImovel}.zip");
        await File.WriteAllBytesAsync(zipPath, zipResult.Data!, cancellationToken);

        logger.LogInformation("ZIP salvo em: {Path}", zipPath);
        logger.LogInformation("Tamanho: {Size} bytes", zipResult.Data!.Length);

        Console.WriteLine();

        // Opção 2: Baixar um CSV específico
        logger.LogInformation("Passo 3: Baixando CSV de parcela...");

        var csvResult = await authService.DownloadCsvAsync(codigoImovel, "parcela", cancellationToken);

        if (!csvResult.Success)
        {
            logger.LogError("Falha ao baixar CSV: {Error}", csvResult.ErrorMessage);
            return;
        }

        var csvPath = Path.Combine(Directory.GetCurrentDirectory(), $"{codigoImovel}_parcela.csv");
        await File.WriteAllBytesAsync(csvPath, csvResult.Data!, cancellationToken);

        logger.LogInformation("CSV salvo em: {Path}", csvPath);
        logger.LogInformation("Tamanho: {Size} bytes", csvResult.Data!.Length);

        Console.WriteLine();
        logger.LogInformation("=== Demo concluída com sucesso! ===");
        
        // Aguarda logs serem escritos
        await Task.Delay(100, cancellationToken);
        
        // Pergunta se quer fazer logout
        Console.WriteLine();
        Console.WriteLine("Deseja fazer logout da sessão? (s/n)");
        Console.Write("> ");
        var resposta = Console.ReadLine()?.Trim().ToLowerInvariant();
        
        if (resposta is "s" or "sim" or "y" or "yes")
        {
            await PerformLogoutAsync(authService, logger, cancellationToken);
        }
        else
        {
            Console.WriteLine("Sessão mantida ativa.");
        }
    }

    private static async Task PerformLogoutAsync(
        IAuthenticationService authService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Realizando logout...");
        
        var logoutResult = await authService.LogoutAsync(cancellationToken);
        
        await Task.Delay(100, cancellationToken);
        
        if (logoutResult.Success)
        {
            Console.WriteLine("✅ Logout realizado com sucesso!");
        }
        else
        {
            Console.WriteLine($"❌ Falha no logout: {logoutResult.ErrorMessage}");
        }
    }
}

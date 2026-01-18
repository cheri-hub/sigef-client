# Cliente SIGEF

Cliente C# para autenticação e download de arquivos do SIGEF (Sistema de Gestão Fundiária) via API GovAuth.

## Requisitos

- .NET 9.0 SDK
- Chrome browser instalado

## Instalação

1. Clone o repositório:
```bash
git clone https://github.com/cheri-hub/sigef-client.git
cd sigef-client
```

2. Restaure os pacotes:
```bash
dotnet restore
```

3. Instale os browsers do Playwright:
```bash
pwsh bin/Debug/net9.0/playwright.ps1 install
```

## Configuração Segura

### Opção 1: User Secrets (Recomendado para desenvolvimento)

Configure a API Key usando User Secrets (nunca commitada no repositório):

```bash
dotnet user-secrets set "SigefClient:ApiKey" "sua-api-key-aqui"
```

### Opção 2: Variáveis de Ambiente

Configure a variável de ambiente:
```bash
# Windows PowerShell
$env:SIGEF_SigefClient__ApiKey = "sua-api-key-aqui"

# Linux/macOS
export SIGEF_SigefClient__ApiKey="sua-api-key-aqui"
```

### Opção 3: appsettings.json (Apenas para testes locais)

⚠️ **NUNCA** commite o arquivo com a API Key real!

Edite `appsettings.json`:
```json
{
  "SigefClient": {
    "ApiKey": "sua-api-key-aqui"
  }
}
```

## Executando

```bash
dotnet run
```

O programa irá:
1. Verificar se você está autenticado na API
2. Se não estiver, abrir o Chrome automaticamente
3. Navegar até o SIGEF e clicar em "Acessar com Gov.br"
4. Aguardar você autenticar com certificado digital
5. Capturar os cookies/tokens de autenticação
6. Enviar os dados para a API
7. Demonstrar download de arquivos (ZIP e CSV)

## Estrutura do Projeto

```
SigefClient/
├── Application/
│   └── Services/
│       └── AuthenticationService.cs    # Orquestra autenticação
├── Configuration/
│   └── SigefClientOptions.cs           # Opções de configuração
├── Domain/
│   ├── Entities/
│   │   └── AuthEntities.cs             # DTOs e entidades
│   └── Interfaces/
│       ├── IAuthenticationService.cs   # Interface do serviço principal
│       ├── IBrowserAutomationService.cs # Interface de automação
│       └── ISigefApiClient.cs          # Interface do cliente HTTP
├── Infrastructure/
│   ├── Browser/
│   │   └── PlaywrightBrowserService.cs # Implementação Playwright
│   └── Http/
│       └── SigefApiClient.cs           # Cliente HTTP
├── appsettings.json                    # Configurações
├── Program.cs                          # Ponto de entrada
├── README.md                           # Este arquivo
└── SigefClient.csproj                  # Projeto .NET 9
```

## Arquitetura

O projeto segue os princípios SOLID e Clean Architecture:

- **Domain**: Interfaces e entidades (sem dependências externas)
- **Application**: Serviços de aplicação (regras de negócio)
- **Infrastructure**: Implementações concretas (HTTP, Playwright)

### Injeção de Dependência

Todos os serviços são injetados via `Microsoft.Extensions.DependencyInjection`:

- `ISigefApiClient` → `SigefApiClient`
- `IBrowserAutomationService` → `PlaywrightBrowserService`
- `IAuthenticationService` → `AuthenticationService`

### Configuração

Usa o padrão Options do .NET com múltiplas fontes:
1. `appsettings.json` (valores padrão)
2. `appsettings.{Environment}.json` (por ambiente)
3. User Secrets (desenvolvimento seguro)
4. Variáveis de ambiente (`SIGEF_*`)
5. Argumentos de linha de comando

## Uso Programático

```csharp
using Microsoft.Extensions.DependencyInjection;
using SigefClient.Domain.Interfaces;

// Obtém o serviço via DI
var authService = serviceProvider.GetRequiredService<IAuthenticationService>();

// Garante autenticação
var authResult = await authService.EnsureAuthenticatedAsync();
if (!authResult.Success)
{
    Console.WriteLine($"Erro: {authResult.ErrorMessage}");
    return;
}

// Baixa todos os arquivos como ZIP
var zipResult = await authService.DownloadAllFilesAsync("codigo-imovel");
await File.WriteAllBytesAsync("parcela.zip", zipResult.Data!);

// OU baixa CSV específico
var csvResult = await authService.DownloadCsvAsync("codigo-imovel", "parcela");
await File.WriteAllBytesAsync("parcela.csv", csvResult.Data!);
```

## Endpoints da API Suportados

| Endpoint | Descrição |
|----------|-----------|
| `GET /v1/auth/status` | Verifica status de autenticação |
| `POST /v1/auth/browser-login` | Inicia autenticação via browser |
| `POST /v1/auth/browser-callback` | Callback com dados de autenticação |
| `GET /v1/sigef/arquivo/todos/{codigo}` | Download ZIP com todos os arquivos |
| `GET /v1/sigef/arquivo/csv/{codigo}/{tipo}` | Download CSV específico (parcela/vertice/limite) |

## Licença

MIT

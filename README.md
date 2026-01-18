# SIGEF Client - Cliente C# com Playwright

Cliente C# para autenticação automática no SIGEF/Gov.br usando Playwright.

Funciona **exatamente igual** à API Python - abre o Chrome do sistema, aguarda login com certificado digital, e captura cookies/localStorage/JWT automaticamente.

## 🚀 Requisitos

- .NET 8.0 SDK
- Google Chrome instalado
- Certificado digital A1 (instalado no Windows)

## 📦 Instalação

```bash
# Clone o repositório
git clone https://github.com/cheri-hub/sigef-client.git
cd sigef-client

# Restaure as dependências
dotnet restore

# Instale o Playwright (browsers)
dotnet build
cd bin/Debug/net8.0
.\playwright.ps1 install chromium
cd ../../..
```

## ⚙️ Configuração

Edite o arquivo `Program.cs` e configure:

```csharp
private const string API_BASE_URL = "https://govauth.cherihub.cloud/api";
private const string API_KEY = "sua-api-key-aqui";
```

## 🔐 Como Funciona

1. **Executa o cliente**: `dotnet run`
2. **Chrome abre automaticamente** com a página do SIGEF
3. **Usuário seleciona certificado** digital na janela do Windows
4. **Faz login no Gov.br** normalmente
5. **Cliente detecta o login** automaticamente
6. **Captura cookies, localStorage e JWT**
7. **Envia para a API** e salva `storage_state.json`
8. **Pronto!** Pode fazer download de arquivos do SIGEF

## 📁 Estrutura

```
sigef-client/
├── Program.cs                  # Ponto de entrada
├── PlaywrightAuthClient.cs     # Cliente com Playwright (autenticação automática)
├── GovAuthClient.cs            # Cliente HTTP simples (alternativo)
├── GovAuthClient.csproj        # Projeto .NET 8
└── README.md                   # Este arquivo
```

## 🎯 Uso

```bash
dotnet run
```

### Exemplo de saída:

```
╔═══════════════════════════════════════════════════════════╗
║     Gov-Auth API - Cliente C# com Playwright              ║
║     Autenticação automática igual à API Python            ║
╚═══════════════════════════════════════════════════════════╝

[1] Verificando status da sessão atual...
    Autenticado: False
    Mensagem: Nenhuma sessão válida encontrada

[2] Iniciando autenticação via Playwright...
    (O Chrome será aberto automaticamente)

🔐 Iniciando autenticação com Playwright...

✓ Chrome aberto
📡 Navegando para SIGEF...
🔍 Procurando botão de login...
   ✓ Clicado: button.sign-in

⏳ Aguardando autenticação...
   → Selecione seu certificado digital
   → Complete o login no Gov.br

✓ Cookie de sessão detectado!

📦 Capturando dados de autenticação...
   ✓ 13 cookies capturados
   ✓ 0 itens do localStorage capturados
   ✓ Storage state salvo em: C:\Users\...\GovAuth\storage_state.json

📤 Enviando dados para a API...
   ✓ Dados enviados com sucesso!

✅ Autenticação concluída com sucesso!

[3] Testando download de arquivos do SIGEF...

    Digite o código da parcela: f7fd7a57-4858-4453-b132-74e74dee2101

📥 Baixando arquivos da parcela: f7fd7a57-4858-4453-b132-74e74dee2101
   ✓ Download concluído: 122,768 bytes

    💾 Arquivo salvo: C:\repo\sigef-client\parcela_f7fd7a57.zip

╔═══════════════════════════════════════════════════════════╗
║                    Teste Concluído!                       ║
╚═══════════════════════════════════════════════════════════╝
```

## 🔧 API Endpoints Utilizados

| Endpoint | Descrição |
|----------|-----------|
| `GET /v1/auth/status` | Verifica se há sessão autenticada |
| `POST /v1/auth/browser-login` | Inicia sessão de autenticação |
| `POST /v1/auth/browser-callback` | Envia cookies capturados |
| `GET /v1/sigef/arquivo/todos/{codigo}` | Baixa todos os CSVs em ZIP |

## 📝 Licença

MIT License - Uso livre para fins comerciais e pessoais.

## 🤝 Contribuições

Pull requests são bem-vindos!

---

Desenvolvido para uso com a [Gov-Auth API](https://github.com/cheri-hub/sigef-api).

using System.Text.Json;
using System.Text;
using System.Net.Http.Json; // Necessário para métodos de extensão para JSON, se você usar.

// --- 1. CONFIGURAÇÕES E CONSTANTES ---

const string ToolSchema = @"{
    ""name"": ""get_server_time"",
    ""description"": ""Busca a data e hora atual do servidor local. Use para responder perguntas sobre a hora atual do servidor. Não use para outras perguntas que não envolvam a hora."",
    ""parameters"": {
        ""type"": ""object"",
        ""properties"": {}
    }
}";

// Url da API para tempo
const string TimeApiUrl = "http://localhost:5038/time/now";
// Url da API de geração do ollama.
const string OllamaApiUrl = "http://localhost:11434/api/chat"; // Usamos /api/chat que suporta histórico e tools

// --- 2. INICIALIZAÇÃO E LOOP PRINCIPAL DO CHAT ---

using var httpClient = new HttpClient();
Console.WriteLine("Bem-vindo ao chat teste da Imagine Software");
Console.WriteLine("O que gostaria de perguntar?");
Console.WriteLine("------------------------------------------");

while (true)
{
    Console.Write("Você: ");
    string? userInput = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(userInput)) continue;

    try
    {
        // 1. Primeira Chamada ao Ollama (Decisão de Uso da Ferramenta)
        Console.WriteLine("⚙️ Enviando prompt ao LLM para decisão...");
        string ollamaResponse = await SendToOllama(httpClient, userInput, ToolSchema, null);

        var responseJson = JsonDocument.Parse(ollamaResponse);
       
        // Verifica se a resposta contém uma chamada de ferramenta (Tool Call)
        if (HasToolCall(responseJson, out var toolName))
        {
            Console.WriteLine($"🤖 LLM solicitou a ferramenta: '{toolName}'");

            // --- 2. Executar o MCP (Chamar a API Externa) ---
            string apiResult = await CallMcpServer(httpClient, TimeApiUrl);
            Console.WriteLine($"   => MCP obteve o dado: {apiResult}");
           
            // --- 3. Segunda Chamada ao Ollama (Contextualização) ---
            // Envia o resultado da API de volta ao LLM para formular a resposta final.
            Console.WriteLine("⚙️ Enviando resultado do MCP de volta ao LLM para resposta final...");
            string finalResponse = await SendToOllama(httpClient, userInput, ToolSchema, apiResult);
           
            // --- 4. Exibir a Resposta Final ---
            Console.WriteLine($"\nIA: {ExtractResponseText(JsonDocument.Parse(finalResponse))}");
        }
        else
        {
            // Se o Ollama respondeu diretamente (sem ferramenta)
            Console.WriteLine($"\nIA: {ExtractResponseText(responseJson)}");
        }
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"\n[ERRO FATAL] Falha na comunicação: Verifique se o Ollama ou o McpServer estão rodando. Detalhe: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[ERRO] Ocorreu um erro: {ex.Message}");
    }
   
    Console.WriteLine("------------------------------------------");
}

// --- 3. FUNÇÕES AUXILIARES ---

/// <summary>
/// Envia o prompt e o esquema da ferramenta para o Ollama.
/// </summary>
async Task<string> SendToOllama(HttpClient client, string prompt, string toolSchema, string? toolResult)
{
    // A API 'chat' do Ollama usa um array de mensagens para gerenciar contexto/histórico.
    var messages = new List<object>
    {
        new { role = "user", content = prompt }
    };

    if (!string.IsNullOrEmpty(toolResult))
    {
        // Se este é o segundo passo, adicionamos o resultado da ferramenta ao contexto.
        // O LLAMA3/Ollama espera um formato específico para Tool Results.
        messages.Add(new
        {
            role = "tool",
            content = toolResult
        });
    }
   
    // O corpo da requisição que inclui o modelo, as mensagens e as ferramentas (MCP).
    var requestBody = new
    {
        model = "llama3.2:3b", //Modelo utilizado que melhor funcionou em minha máquina e supriu os requisitos do teste
        messages = messages,
        stream = false, // Não queremos streaming para simplificar a lógica de detecção de Tool Call
        tools = new[] { JsonDocument.Parse(toolSchema).RootElement }
    };

    string jsonContent = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

    var response = await client.PostAsync(OllamaApiUrl, content);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

/// <summary>
/// Executa a chamada real para a API no seu McpServer.
/// </summary>
async Task<string> CallMcpServer(HttpClient client, string url)
{
    HttpResponseMessage response = await client.GetAsync(url);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

/// <summary>
/// Verifica se a resposta do Ollama contém uma solicitação de ferramenta.
/// </summary>
bool HasToolCall(JsonDocument doc, out string toolName)
{
    toolName = string.Empty;
    try
    {
        // O Ollama retorna a chamada de ferramenta dentro do objeto 'message'
        var toolCalls = doc.RootElement.GetProperty("message").GetProperty("tool_calls");
       
        if (toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
        {
            // Estamos interessados apenas no nome da primeira ferramenta chamada
            toolName = toolCalls[0].GetProperty("function").GetProperty("name").GetString() ?? string.Empty;
            return true;
        }
        return false;
    }
    catch (KeyNotFoundException)
    {
        // 'tool_calls' não existe, então não há chamada de ferramenta
        return false;
    }
    catch (Exception)
    {
        // Outro erro de parsing JSON
        return false;
    }
}

/// <summary>
/// Extrai o texto de resposta final (content) do JSON do Ollama.
/// </summary>
string ExtractResponseText(JsonDocument doc)
{
    try
    {
        return doc.RootElement
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? "Sem resposta explícita do LLM.";
    }
    catch (Exception)
    {
        return "Erro ao analisar a resposta do LLM.";
    }
}

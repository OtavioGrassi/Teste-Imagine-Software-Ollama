using System.Text;
using System.Text.Json;

//  CONFIGURAÇÕES

const string TimeApiUrl = "http://localhost:5038/time/now";
const string OllamaApiUrl = "http://localhost:11434/api/chat";

using var httpClient = new HttpClient();

Console.WriteLine("Bem-vindo ao chat teste da Imagine Software");
Console.WriteLine("O que gostaria de perguntar?");

//  LOOP PRINCIPAL DO CHAT

while (true)
{
    Console.Write("Digite sua dúvida: ");
    string? userInput = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(userInput)) continue;

    try
    {
        // Decide se a ferramenta deve estar disponível
        bool shouldIncludeTools = UserIsAskingForTime(userInput);

        Console.WriteLine("⚙️ Enviando prompt ao LLM para decisão...");
        string firstResponse = await SendToOllama(httpClient, userInput, shouldIncludeTools, null);

        var json = JsonDocument.Parse(firstResponse);

        // Verifica se houve necessidade do tool_call
        if (shouldIncludeTools && HasToolCall(json, out string toolName))
        {
            Console.WriteLine($"🤖 LLM solicitou a ferramenta: '{toolName}'");

            // Chama o MCP
            string apiResult = await CallMcpServer(httpClient, TimeApiUrl);
            Console.WriteLine($"   => MCP obteve o dado: {apiResult}");

            Console.WriteLine("⚙️ Enviando resultado do MCP ao LLM...");

            // Segunda chamada sem tools
            string finalResponse = await SendToOllama(httpClient, userInput, includeTools: false, toolResult: apiResult);

            Console.WriteLine($"\nIA: {ExtractResponseText(JsonDocument.Parse(finalResponse))}");
        }
        else
        {
            // Resposta direta quando não necessita do MCP
            Console.WriteLine($"\nIA: {ExtractResponseText(json)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[ERRO] {ex.Message}");
    }

    Console.WriteLine("Gostaria de fazer outra pergunta?");
}

//  FUNÇÕES

//Foi necessario adicionar essas instruções para o modelo llama utilizado: "llama3.2:3b", sem isso ele requisitava o MCP para quealquer pergunta, o que iria contra o objetivo do teste. Ele é uma modelo mais leve e o que melhor rodou em minha máquina, porém mais fraco e comete várias inconsistências se não for bem configurado.

bool UserIsAskingForTime(string input)
{
    input = input.ToLower();

    string[] keywords = new[]  
    {
        "que horas",
        "hora",
        "horário",
        "agora",
        "data atual",
        "data de hoje",
        "dia de hoje",
        "momento atual",
        "horas são",
        "hora atual"
    };

    return keywords.Any(k => input.Contains(k));
}

async Task<string> SendToOllama(HttpClient client, string prompt, bool includeTools, string? toolResult)
{
    var messages = new List<object>
    {
        new {
            role = "system",
            content =
            "Você só deve chamar a ferramenta 'get_server_time' quando o usuário perguntar explicitamente sobre a hora ou data atual."
        },
        new { role = "user", content = prompt }
    };

    if (!string.IsNullOrEmpty(toolResult))
    {
        // Reforço importante para a SEGUNDA chamada ao LLM
        messages.Insert(1, new
        {
            role = "system",
            content = "Você recebeu o resultado da ferramenta. Responda APENAS usando esse resultado. Não forneça código. Não forneça explicações extras. Apenas responda claramente ao usuário com base no dado retornado."
        });

        messages.Add(new { role = "tool", content = toolResult });
    }

    object? tools = null;

    if (includeTools)
    {
        tools = new[] {
            new {
                type = "function",
                function = new {
                    name = "get_server_time",
                    description = "Retorna a data e hora atual do servidor.",
                    parameters = new {
                        type = "object",
                        properties = new {}
                    }
                }
            }
        };
    }

    var body = new
    {
        model = "llama3.2:3b",
        messages = messages,
        stream = false,
        tools = tools
    };

    string json = JsonSerializer.Serialize(body);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await client.PostAsync(OllamaApiUrl, content);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

async Task<string> CallMcpServer(HttpClient client, string url)
{
    var response = await client.GetAsync(url);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

bool HasToolCall(JsonDocument doc, out string toolName)
{
    toolName = string.Empty;

    try
    {
        var toolCalls = doc.RootElement.GetProperty("message").GetProperty("tool_calls");

        if (toolCalls.ValueKind == JsonValueKind.Array &&
            toolCalls.GetArrayLength() > 0)
        {
            toolName = toolCalls[0].GetProperty("function").GetProperty("name").GetString() ?? "";
            return !string.IsNullOrWhiteSpace(toolName);
        }

        return false;
    }
    catch
    {
        return false;
    }
}

string ExtractResponseText(JsonDocument doc)
{
    try
    {
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString()
               ?? "Sem resposta.";
    }
    catch
    {
        return "Erro ao ler resposta.";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

class PalworldShutdownScheduler
{
    private static readonly HttpClient HttpClient = new HttpClient();
    public static string contato = "120363295786838904@g.us";
    static async Task Main(string[] args)
    {
        Console.WriteLine("[INFO] Inicializando o Agendador de Shutdown...");

        var servers = LoadServerConfigurations();
        foreach (var server in servers)
        {
            ScheduleShutdowns(server);
        }

        Console.WriteLine("[INFO] Agendador configurado. Pressione Enter para sair.");
        Console.ReadLine();
    }

    private static List<ServerConfig> LoadServerConfigurations()
    {
        var servers = new List<ServerConfig>();
        for (int i = 1; ; i++)
        {
            var server = Environment.GetEnvironmentVariable($"API_SERVER_{i}");
            if (string.IsNullOrEmpty(server)) break;

            servers.Add(new ServerConfig
            {
                Name = Environment.GetEnvironmentVariable($"Nome_Server_{i}"),
                ApiUrl = $"http://{server}:{Environment.GetEnvironmentVariable($"API_PORT_{i}")}/v1/api/shutdown",
                Username = Environment.GetEnvironmentVariable($"API_USER_{i}"),
                Password = Environment.GetEnvironmentVariable($"API_PASSWORD_{i}"),
                ScheduleTimes = ParseScheduleTimes(Environment.GetEnvironmentVariable($"SCHEDULE_TIMES_{i}")),
                ShutdownMessage = Environment.GetEnvironmentVariable($"SHUTDOWN_MESSAGE_{i}"),
                WaitTimeMinutes = int.TryParse(Environment.GetEnvironmentVariable($"SHUTDOWN_WAITTIME_{i}"), out var waitTime) ? waitTime : 300
            });
        }

        Console.WriteLine($"[INFO] {servers.Count} servidores configurados.");
        return servers;
    }

    private static List<TimeSpan> ParseScheduleTimes(string scheduleTimes)
    {
        if (string.IsNullOrEmpty(scheduleTimes)) return new List<TimeSpan>();

        return scheduleTimes.Split(',')
                            .Select(time => TimeSpan.TryParse(time.Trim(), out var result) ? result : (TimeSpan?)null)
                            .Where(time => time.HasValue)
                            .Select(time => time.Value)
                            .ToList();
    }

    private static void ScheduleShutdowns(ServerConfig server)
    {
        foreach (var time in server.ScheduleTimes)
        {
            var now = DateTime.Now;
            var schedule = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, 0);
            if (schedule < now) schedule = schedule.AddDays(1);

            var interval = schedule - now;
            var timer = new System.Timers.Timer(interval.TotalMilliseconds) { AutoReset = false };

            timer.Elapsed += async (sender, e) =>
            {
                await PerformShutdown(server);
                ScheduleShutdowns(server); // Reagendar para o próximo dia
            };

            timer.Start();
            Console.WriteLine($"[INFO] Shutdown agendado para o servidor '{server.Name}' às {schedule}.");
            EnviarMensagemWhatsApp($"Shutdown agendado para o servidor '{server.Name}' às {schedule}.", contato);
        }
    }

    private static async Task PerformShutdown(ServerConfig server)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, server.ApiUrl);
            request.Headers.Add("Authorization", server.Password);

            var shutdownMessage = new
            {
                waittime = server.WaitTimeMinutes,
                message = server.ShutdownMessage ?? "Desligamento para atualização de Config em 5 min"
            };

            var content = new StringContent(JsonSerializer.Serialize(shutdownMessage), Encoding.UTF8, "application/json");
            request.Content = content;

            Console.WriteLine($"[INFO] Enviando requisição de shutdown para '{server.Name}'...");
            EnviarMensagemWhatsApp($"Desligamento para atualização de Config em {server.WaitTimeMinutes} min", contato);
            var response = await HttpClient.SendAsync(request);

            response.EnsureSuccessStatusCode();
            Console.WriteLine($"[SUCESSO] Shutdown realizado com sucesso no servidor '{server.Name}': {await response.Content.ReadAsStringAsync()}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXCEÇÃO] Erro ao realizar shutdown no servidor '{server.Name}': {ex.Message}");
            EnviarMensagemWhatsApp($"Erro ao realizar shutdown no servidor '{server.Name}': {ex.Message}", contato);
        }
    }
    static async System.Threading.Tasks.Task EnviarMensagemWhatsApp(string mensagem, string contato)
    {
        // Implementar a lógica para enviar mensagem via WhatsApp aqui
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://sukeserver.ddns.net:3000/client/sendMessage/suke");
        request.Headers.Add("x-api-key", "SukeApiWhatsApp");
        var content = new StringContent("{" + $"\r\n  \"chatId\": \"{contato}\",\r\n  \"contentType\": \"string\",\r\n  \"content\": \"{mensagem}\"\r\n" + "}", null, "application/json");
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response WhatsApp: {response.StatusCode}");
        Console.WriteLine($"Mensagem enviada: {mensagem} para {contato}");
    }

    private class ServerConfig
    {
        public string Name { get; set; }
        public string ApiUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public List<TimeSpan> ScheduleTimes { get; set; } = new List<TimeSpan>();
        public string ShutdownMessage { get; set; }
        public int WaitTimeMinutes { get; set; }
    }


}

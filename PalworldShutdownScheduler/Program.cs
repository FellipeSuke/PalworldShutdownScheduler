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

        // Adiciona servidor local para debug
        //servers.Add(new ServerConfig
        //{
        //    Name = "Local Debug Server",
        //    ApiUrl = "http://localhost:8212/v1/api/shutdown",
        //    Username = "admin",
        //    Password = "joga10",
        //    ScheduleTimes = new List<TimeSpan> { TimeSpan.Parse("21:17"), TimeSpan.Parse("21:18") },
        //    ShutdownMessage = "Teste de desligamento local",
        //    WaitTimeMinutes = 30
        //});

        foreach (var server in servers)
        {
            ScheduleShutdowns(server);
        }

        Console.WriteLine("[INFO] Agendador configurado. Pressione Ctrl+C para sair.");

        // Aguardar indefinidamente para manter o container ativo
        while (true)
        {
            await Task.Delay(10000);  // Delay para não sobrecarregar o CPU, você pode ajustar o tempo.
        }
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
                timer.Stop(); // Para garantir que não será chamado novamente
                await PerformShutdown(server);

                // Reagendar para o próximo dia
                var nextDay = schedule.AddDays(1);
                var newTimer = new System.Timers.Timer((nextDay - DateTime.Now).TotalMilliseconds) { AutoReset = false };
                newTimer.Elapsed += async (s, evt) => await PerformShutdown(server);
                newTimer.Start();

                Console.WriteLine($"[INFO] Shutdown reagendado para o servidor '{server.Name}' às {nextDay}.");
            };

            timer.Start();
            Console.WriteLine($"[INFO] Shutdown agendado para o servidor '{server.Name}' às {schedule}.");
            EnviarMensagemWhatsApp($"Shutdown agendado para o servidor '{server.Name}' às {schedule}.", contato);
        }
    }

    private static async Task PerformShutdown(ServerConfig server)
    {
        using (HttpClient client = new HttpClient())
        {
            string url = $"{server.ApiUrl}";
            Console.WriteLine($"{server.WaitTimeMinutes}    {server.ShutdownMessage}");
            var content = new StringContent($"{{\"waittime\": {server.WaitTimeMinutes}, \"message\": \"{server.ShutdownMessage}\"}}", Encoding.UTF8, "application/json");

            // Codifica o usuário e senha em Base64 para a autenticação Basic
            string authInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{server.Username}:{server.Password}"));
            client.DefaultRequestHeaders.Add("Authorization", $"Basic {authInfo}");

            // Envia a requisição POST
            HttpResponseMessage response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Reinício solicitado com sucesso.");
            }
            else
            {
                Console.WriteLine($"Falha ao solicitar reinício. Status: {response.StatusCode}");
            }
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

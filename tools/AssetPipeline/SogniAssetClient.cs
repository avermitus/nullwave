using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SogniAssetClient
{
    const string Endpoint = "https://api.sogni.ai/v1/creative-agent/workflows";

    static async Task<int> Main(string[] args)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(25));
            return await Run(args, deadline.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error is HttpRequestException http
                ? $"Network request failed (HTTP {http.StatusCode})."
                : error.Message);
            return 1;
        }
    }

    static async Task<int> Run(string[] args, CancellationToken cancel)
    {
        if (args.Length != 2 || args[0] is not ("generate" or "resume"))
            throw new ArgumentException("From the repository root: AssetPipeline generate <prompt.txt> | resume <workflowId>");
        string root = Path.GetFullPath(".");
        if (!File.Exists(Path.Combine(root, "src", "Nullwave", "Nullwave.csproj")))
            throw new InvalidOperationException("Start the tool in the Nullwave repository root.");
        string? key = Environment.GetEnvironmentVariable("SOGNI_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Set SOGNI_API_KEY in the process environment.");
        using var api = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        string id = args[1];
        if (args[0] == "generate")
        {
            string prompt = (await File.ReadAllTextAsync(args[1], cancel)).Trim();
            if (prompt.Length == 0) throw new InvalidDataException("Prompt is empty.");
            var step = new { id = "atlas", toolName = "generate_image", arguments = new { prompt, model = "z-turbo", seed = 731091, aspectRatio = "1:1" } };
            string body = JsonSerializer.Serialize(new { input = new { title = "Nullwave material surface", steps = new[] { step } }, token_type = "spark" });
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("Idempotency-Key", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
            using var response = await api.SendAsync(request, cancel);
            var workflow = await ReadWorkflow(response, cancel);
            id = (string?)workflow["workflowId"] ?? throw new InvalidDataException("Missing workflow ID.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,192}\z"))
            throw new ArgumentException("Invalid workflow ID.");
        Console.WriteLine($"Workflow {id}. Resume with: AssetPipeline resume {id}");
        while (true)
        {
            using var result = await api.GetAsync($"{Endpoint}/{id}", cancel);
            if ((int)result.StatusCode is 429 or 503)
            {
                var retry = result.Headers.RetryAfter;
                var wait = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(15);
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1), cancel);
                continue;
            }
            var workflow = await ReadWorkflow(result, cancel);
            var status = (string?)workflow["status"];
            if (status == "completed")
            {
                var artifact = (workflow["artifacts"] as JsonArray)?.FirstOrDefault(item => (string?)item?["kind"] == "image");
                if (!Uri.TryCreate((string?)artifact?["url"], UriKind.Absolute, out var url) || url.Scheme != "https")
                    throw new InvalidDataException("Completed workflow has no downloadable image.");
                var project = workflow["steps"]?[0]?["cost"]?["actual"]?["project"];
                if (project?["costActual"]?["costInToken"] is {} cost)
                    Console.WriteLine($"Reported project cost: {cost} {project["tokenType"]}");
                using var downloads = new HttpClient(); // Signed media URLs never receive the API credential.
                byte[] bytes = await downloads.GetByteArrayAsync(url, cancel);
                string extension = bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) ? ".png"
                    : bytes.AsSpan().StartsWith(new byte[] {255,216,255}) ? ".jpg"
                    : throw new InvalidDataException("Downloaded artifact is not PNG or JPEG.");
                string folder = Directory.CreateDirectory(Path.Combine(root, "Assets", "Textures", "Generated")).FullName;
                string path = Path.Combine(folder, id + extension);
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(tmp, bytes, cancel);
                    File.Move(tmp, path, true);
                }
                finally { File.Delete(tmp); }
                Console.WriteLine(path);
                return 0;
            }
            if (status is not ("running" or "queued"))
                throw new InvalidOperationException($"Workflow {id} reached {status} ({workflow["waitingReason"]}).");
            await Task.Delay(TimeSpan.FromSeconds(6), cancel);
        }
    }

    static async Task<JsonNode> ReadWorkflow(HttpResponseMessage response, CancellationToken cancel)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Sogni returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }
        var envelope = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancel);
        return envelope?["data"]?["workflow"] ?? throw new InvalidDataException("Missing workflow snapshot.");
    }

}

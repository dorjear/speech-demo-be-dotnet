using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using Newtonsoft.Json;

using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

[ApiController]
[Route("api/[controller]")]
public class VoiceController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<VoiceController> _logger;
    private readonly IHttpClientFactory _clientFactory;

    public VoiceController(IHttpClientFactory clientFactory, IConfiguration configuration, ILogger<VoiceController> logger)
    {
        _configuration = configuration;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File is not provided or empty.");
        }

        var client = _clientFactory.CreateClient();

        var apiKey = _configuration["OpenAI:apiKey"];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var content = new MultipartFormDataContent();

        var fileContent = new StreamContent(file.OpenReadStream());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");

        var modelContent = new StringContent("whisper-1");
        content.Add(modelContent, "model");
        content.Add(fileContent, "file", file.FileName);

        try
        {
            HttpResponseMessage response = await client.PostAsync("https://api.openai.com/v1/audio/transcriptions", content);
            response.EnsureSuccessStatusCode();

            string jsonResponse = await response.Content.ReadAsStringAsync();
            _logger.LogInformation(jsonResponse);
            return Ok(jsonResponse);

        }
        catch (HttpRequestException ex)
        {
            // Print the error content if there is an HTTP request exception
            _logger.LogError(ex, "An error occurred while processing the file");
            throw;
        }
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] UserMessage request)
    {
        // OpenAI API configuration
        var openAiUrl = "https://api.openai.com/v1/chat/completions";
        var openAiApiKey = _configuration["OpenAI:apiKey"];
        // Prepare request body for OpenAI API
        var openAiRequestBody = new
            {
                model = "gpt-4o-mini", // Replace with the correct model ID
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user", content = request.Message }
                }
            };

        var content = new StringContent(JsonConvert.SerializeObject(openAiRequestBody), Encoding.UTF8, "application/json");

        // Add Authorization header
        var client = _clientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");

        // Send request to OpenAI API
        var response = await client.PostAsync(openAiUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("response is "+responseBody);
        // Deserialize the response from OpenAI API
        var openAiResponse = JsonConvert.DeserializeObject<OpenAiChatResponse>(responseBody);

        // Return the response as JSON
        return Ok(new { response = openAiResponse.Choices[0].Message.Content.Trim() });

    }

    [HttpPost("chatWithVoice")]
    public async Task<IActionResult> ChatWithVoice([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("File is not provided or empty.");
        }

        // Save uploaded webm to temp file
        var tempWebmPath = Path.GetTempFileName() + ".webm";
        var tempWavPath = Path.GetTempFileName() + ".wav";
        try
        {
            using (var stream = new FileStream(tempWebmPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Convert webm to wav using ffmpeg
            var ffmpegPath = "ffmpeg"; // Assumes ffmpeg is in PATH
            var ffmpegArgs = $"-y -i \"{tempWebmPath}\" -ar 16000 -ac 1 -f wav \"{tempWavPath}\"";
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = ffmpegArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string ffmpegOutput = await process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg conversion failed: " + ffmpegOutput);
                return StatusCode(500, "Audio conversion failed.");
            }

            // Read wav and base64 encode
            byte[] wavBytes = await System.IO.File.ReadAllBytesAsync(tempWavPath);
            string wavBase64 = Convert.ToBase64String(wavBytes);

            // Build OpenAI request
            var openAiUrl = "https://api.openai.com/v1/chat/completions";
            var openAiApiKey = _configuration["OpenAI:apiKey"];
            var openAiRequestBody = new
            {
                model = "gpt-4o-audio-preview",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Please answer the query of this audio. No need to explain the input." },
                            new
                            {
                                type = "input_audio",
                                input_audio = new
                                {
                                    data = wavBase64,
                                    format = "wav"
                                }
                            }
                        }
                    }
                }
            };

            var content = new StringContent(JsonConvert.SerializeObject(openAiRequestBody), Encoding.UTF8, "application/json");
            var client = _clientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);

            var response = await client.PostAsync(openAiUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("OpenAI response: " + responseBody);

            // Map OpenAI response to { text: ... }
            var openAiResponse = JsonConvert.DeserializeObject<OpenAiChatResponse>(responseBody);
            string text = openAiResponse?.Choices?[0]?.Message?.Content?.Trim() ?? "";

            return Ok(new { text });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in chatWithVoice");
            return StatusCode(500, "Internal server error: " + ex.Message);
        }
        finally
        {
            // Clean up temp files
            try { if (System.IO.File.Exists(tempWebmPath)) System.IO.File.Delete(tempWebmPath); } catch { }
            try { if (System.IO.File.Exists(tempWavPath)) System.IO.File.Delete(tempWavPath); } catch { }
        }
    }
}


public record UserMessage(string Message);

public class OpenAiChatResponse
{
    public Choice[] Choices { get; set; }
}

public class Choice
{
    public Message Message { get; set; }
}

public class Message
{
    public string Role { get; set; }
    public string Content { get; set; }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Net.Http.Headers;

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

}

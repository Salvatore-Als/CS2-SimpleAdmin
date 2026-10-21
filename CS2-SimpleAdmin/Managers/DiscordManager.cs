using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CS2_SimpleAdmin.Managers;

public class DiscordManager(string webhookUrl)
{

    /// <summary>
    /// Sends a plain text message asynchronously to the configured Discord webhook URL.
    /// </summary>
    /// <param name="message">The text message to send to Discord.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public async Task SendMessageAsync(string message)
    {
        HttpClient client = CS2_SimpleAdmin.HttpClient;
        Dictionary<string, object> payload = new Dictionary<string, object>
        {
            ["content"] = message
        };

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        string json = JsonSerializer.Serialize(payload, options);
        StringContent content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage response = await client.PostAsync(webhookUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                CS2_SimpleAdmin._logger?.LogError(
                    $"Failed to send discord message. Status Code: {response.StatusCode}, Reason: {response.ReasonPhrase}");
            }
        }
        catch (HttpRequestException e)
        {
            CS2_SimpleAdmin._logger?.LogError($"Error sending discord message: {e.Message}");
        }
    }

    /// <summary>
    /// Sends an embed message asynchronously to the configured Discord webhook URL.
    /// </summary>
    /// <param name="embed">The embed object containing rich content to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public async Task SendEmbedAsync(Embed embed)
    {
        HttpClient httpClient = CS2_SimpleAdmin.HttpClient;

        // Same JSON as before: every key is written, null when the part is empty
        Dictionary<string, object?> embedPayload = new Dictionary<string, object?>
        {
            ["color"] = embed.Color,
            ["title"] = null,
            ["description"] = null,
            ["thumbnail"] = null,
            ["image"] = null,
            ["footer"] = null,
            ["timestamp"] = embed.Timestamp,
            ["fields"] = null
        };

        if (!string.IsNullOrEmpty(embed.Title))
        {
            embedPayload["title"] = embed.Title;
        }

        if (!string.IsNullOrEmpty(embed.Description))
        {
            embedPayload["description"] = embed.Description;
        }

        if (!string.IsNullOrEmpty(embed.ThumbnailUrl))
        {
            embedPayload["thumbnail"] = new Dictionary<string, object?> { ["url"] = embed.ThumbnailUrl };
        }

        if (!string.IsNullOrEmpty(embed.ImageUrl))
        {
            embedPayload["image"] = new Dictionary<string, object?> { ["url"] = embed.ImageUrl };
        }

        if (embed.Footer != null && !string.IsNullOrEmpty(embed.Footer.Text))
        {
            embedPayload["footer"] = new Dictionary<string, object?> { ["text"] = embed.Footer.Text, ["icon_url"] = embed.Footer.IconUrl };
        }

        if (embed.Fields.Count > 0)
        {
            embedPayload["fields"] = embed.Fields
                .Select(field => new Dictionary<string, object?> { ["name"] = field.Name, ["value"] = field.Value, ["inline"] = field.Inline })
                .ToArray();
        }

        Dictionary<string, object> payload = new Dictionary<string, object>
        {
            ["embeds"] = new[] { embedPayload }
        };

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        string jsonPayload = JsonSerializer.Serialize(payload, options);
        StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await httpClient.PostAsync(webhookUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorMessage = await response.Content.ReadAsStringAsync();
            CS2_SimpleAdmin._logger?.LogError($"Failed to send embed: {response.StatusCode} - {errorMessage}");
        }
    }

    /// <summary>
    /// Converts a hexadecimal color string (e.g. "#FF0000") to its integer representation.
    /// </summary>
    /// <param name="hex">The hexadecimal color string, optionally starting with '#'.</param>
    /// <returns>An integer representing the color.</returns>
    public static int ColorFromHex(string hex)
    {
        if (hex.StartsWith($"#"))
        {
            hex = hex[1..];
        }

        return int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
    }
}

/// <summary>
/// Represents a Discord embed message containing rich content such as title, description, fields, and images.
/// </summary>
public class Embed
{
    public int Color { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public Footer? Footer { get; init; }
    public string? Timestamp { get; init; }

    public List<EmbedField> Fields { get; } = [];

    /// <summary>
    /// Adds a field to the embed message.
    /// </summary>
    /// <param name="name">The name of the field.</param>
    /// <param name="value">The value or content of the field.</param>
    /// <param name="inline">Whether the field should be displayed inline with other fields.</param>
    public void AddField(string name, string value, bool inline)
    {
        EmbedField field = new EmbedField
        {
            Name = name,
            Value = value,
            Inline = inline
        };

        Fields.Add(field);
    }
}

/// <summary>
/// Represents the footer section of a Discord embed message, including optional text and icon URL.
/// </summary>
public class Footer
{
    public string? Text { get; init; }
    public string? IconUrl { get; set; }
}

/// <summary>
/// Represents a field inside a Discord embed message.
/// </summary>
public class EmbedField
{
    public string? Name { get; init; }
    public string? Value { get; init; }
    public bool Inline { get; init; }
}


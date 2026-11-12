using System.Text;
using System.Text.Json;

namespace MyFiles.Upload;

/// <summary>
/// Разбор ответа сервера вида {"ok":true,"uploaded":[...],"failed":[...]}.
/// Элементы uploaded/failed могут быть строками или объектами — поддержаны оба варианта.
/// </summary>
public static class ServerResponse
{
    private static readonly string[] ConflictMarkers =
    {
        "существ", "exist", "already", "duplicate", "конфликт", "занят", "перезапис"
    };

    public static UploadResult Parse(int statusCode, string body)
    {
        JsonElement root = default;
        bool hasJson = TryParseJson(body, out root);

        if (statusCode is >= 200 and < 300)
        {
            if (!hasJson)
            {
                return Fail("Неожиданный ответ сервера (не JSON): " + Snippet(body));
            }

            bool ok = root.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.True;
            List<string> failed = ReadMessages(root, "failed");
            List<string> uploaded = ReadMessages(root, "uploaded");

            if (failed.Count > 0)
            {
                return Fail(string.Join("; ", failed));
            }
            if (ok)
            {
                string name = uploaded.Count > 0 && uploaded[0].Length > 0 ? uploaded[0] : "";
                return new UploadResult(true, name.Length > 0 ? "Загружено: " + name : "Загружено");
            }
            string error = ReadString(root, "error");
            return Fail(error.Length > 0 ? error : "Сервер вернул ok=false без пояснений.");
        }

        // Нестандартный код HTTP: попробуем достать текст ошибки из тела.
        string detail = "";
        if (hasJson)
        {
            List<string> failed = ReadMessages(root, "failed");
            detail = failed.Count > 0 ? string.Join("; ", failed) : ReadString(root, "error");
            if (detail.Length == 0) detail = ReadString(root, "message");
        }
        if (detail.Length == 0) detail = Snippet(body);

        string prefix = statusCode switch
        {
            413 => "Сервер отверг файл: слишком большой (HTTP 413). Проверьте лимиты upload_max_filesize и post_max_size на сервере.",
            401 or 403 => "Сервер запретил загрузку (HTTP " + statusCode + ").",
            >= 500 => "Внутренняя ошибка сервера (HTTP " + statusCode + ").",
            _ => "Ошибка HTTP " + statusCode + "."
        };
        return Fail(prefix + (detail.Length > 0 ? " " + detail : ""));
    }

    private static UploadResult Fail(string message)
    {
        bool conflict = ConflictMarkers.Any(m => message.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (conflict)
        {
            message = "Файл с таким именем уже есть на сервере (существующие файлы не перезаписываются). " +
                      "Переименуйте файл и загрузите снова. Ответ сервера: " + message;
        }
        return new UploadResult(false, message, conflict);
    }

    private static bool TryParseJson(string body, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(body)) return false;
        // Иногда PHP примешивает предупреждения к JSON — ищем первый '{' и последний '}'.
        int start = body.IndexOf('{');
        int end = body.LastIndexOf('}');
        if (start < 0 || end <= start) return false;
        try
        {
            using var doc = JsonDocument.Parse(body.Substring(start, end - start + 1));
            root = doc.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<string> ReadMessages(JsonElement root, string property)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(property, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (JsonElement el in arr.EnumerateArray())
        {
            result.Add(ElementText(el));
        }
        return result;
    }

    private static string ElementText(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString() ?? "";
            case JsonValueKind.Object:
                string name = ReadString(el, "name");
                if (name.Length == 0) name = ReadString(el, "file");
                string error = ReadString(el, "error");
                if (error.Length == 0) error = ReadString(el, "message");
                if (error.Length == 0) error = ReadString(el, "reason");
                if (name.Length > 0 && error.Length > 0) return name + ": " + error;
                if (error.Length > 0) return error;
                if (name.Length > 0) return name;
                return el.GetRawText();
            default:
                return el.GetRawText();
        }
    }

    private static string ReadString(JsonElement obj, string property)
    {
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out JsonElement el)
            && el.ValueKind == JsonValueKind.String
            ? (el.GetString() ?? "")
            : "";
    }

    private static string Snippet(string body)
    {
        body = (body ?? "").Trim();
        if (body.Length == 0) return "(пустой ответ)";
        return body.Length <= 300 ? body : body.Substring(0, 300) + "…";
    }
}

using System.Text.Json;

namespace MyFiles.Upload;

/// <summary>
/// Разбор ответа сервера вида {"ok":true,"uploaded":[...],"failed":[...]}.
/// Устойчив к ok как bool/числу/строке и к примеси вывода до/после JSON.
/// Всегда возвращает HTTP-код и полное тело ответа для диагностики.
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
                return Fail(statusCode, body, "Ответ сервера не является JSON (HTTP " + statusCode + "). Тело: " + Snippet(body));
            }

            bool? ok = GetOk(root);
            List<string> failed = ReadMessages(root, "failed");
            List<string> uploaded = ReadMessages(root, "uploaded");

            if (failed.Count > 0)
            {
                return Fail(statusCode, body, string.Join("; ", failed));
            }

            // Успех: ok=true (в любой форме) ЛИБО ok отсутствует, но uploaded непустой и failed пустой.
            if (ok == true || (ok == null && uploaded.Count > 0))
            {
                string name = uploaded.Count > 0 && uploaded[0].Length > 0 ? uploaded[0] : "";
                return new UploadResult(true, name.Length > 0 ? "Загружено: " + name : "Загружено")
                {
                    StatusCode = statusCode,
                    ResponseBody = body
                };
            }

            string error = ReadString(root, "error");
            return Fail(statusCode, body, error.Length > 0 ? error : "Сервер вернул ok=false без пояснений. Тело: " + Snippet(body));
        }

        // Нестандартный HTTP-код: достаём текст ошибки из тела, если он есть.
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
            301 or 302 or 303 or 307 or 308 => "Перенаправление (HTTP " + statusCode + "), загрузка не завершена.",
            413 => "Сервер отверг файл: слишком большой (HTTP 413). Проверьте лимиты upload_max_filesize и post_max_size на сервере.",
            401 or 403 => "Сервер запретил загрузку (HTTP " + statusCode + ").",
            >= 500 => "Внутренняя ошибка сервера (HTTP " + statusCode + ").",
            _ => "Ошибка HTTP " + statusCode + "."
        };
        return Fail(statusCode, body, prefix + (detail.Length > 0 ? " " + detail : ""));
    }

    private static UploadResult Fail(int statusCode, string body, string message)
    {
        bool conflict = ConflictMarkers.Any(m => message.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (conflict)
        {
            message = "Файл с таким именем уже есть на сервере (существующие файлы не перезаписываются). " +
                      "Переименуйте файл и загрузите снова. Ответ сервера: " + message;
        }
        return new UploadResult(false, message, conflict)
        {
            StatusCode = statusCode,
            ResponseBody = body
        };
    }

    /// <summary>ok как bool/число/строка; null — если поле отсутствует или не распознано.</summary>
    private static bool? GetOk(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out JsonElement v))
        {
            return null;
        }
        switch (v.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return v.TryGetDouble(out double d) ? d != 0 : null;
            case JsonValueKind.String:
                string s = (v.GetString() ?? "").Trim().ToLowerInvariant();
                if (s is "true" or "1" or "yes" or "ok" or "success") return true;
                if (s is "false" or "0" or "no" or "error" or "fail") return false;
                return null;
            default: return null;
        }
    }

    private static bool TryParseJson(string body, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(body)) return false;
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
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
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

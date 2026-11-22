using System.Text.Json;
using System.Text.Json.Nodes;

namespace Suzu.Core;

/// <summary>作業状況のヒント</summary>
public enum ActivityHint { UserFocused, UserIdle, UserAway, Meeting, BreakTime }

/// <summary>鈴秘書 → マスコットへのイベント</summary>
public abstract record SecretaryEvent
{
    /// <summary>ユーザーへの通知（吹き出しで表示）</summary>
    public sealed record Notify(string Title, string Body) : SecretaryEvent;
    /// <summary>会話内容に応じた表情（例: joy, surprise, troubled）</summary>
    public sealed record Expression(Pose Pose, double Duration) : SecretaryEvent;
    /// <summary>作業状況のヒント（例: 集中中は静かにする）</summary>
    public sealed record ActivityChanged(ActivityHint Hint) : SecretaryEvent;
    /// <summary>鈴秘書が話し始めた</summary>
    public sealed record Speak(string Text) : SecretaryEvent;
}

/// <summary>マスコット → 鈴秘書へのイベント</summary>
public enum MascotEventKind { UserClicked, ConversationRequested, UserMessage }

public readonly record struct MascotEvent(MascotEventKind Kind, string? Text = null);

/// <summary>
/// 鈴秘書との JSON の相互変換。形式は macOS 版と同じ:
///   {"type":"notify","title":"…","body":"…"} / {"type":"expression","pose":"joy","duration":3}
///   {"type":"activity","hint":"userFocused"} / {"type":"speak","text":"…"}
/// マスコット側からは {"type":"click"} / {"type":"conversation"} / {"type":"message","text":"…"}
/// </summary>
public static class SecretaryJson
{
    public static SecretaryEvent? Decode(string json)
    {
        JsonObject? obj;
        try { obj = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
        if (obj is null) return null;
        string? Str(string key) => obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        double? Num(string key) => obj[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

        switch (Str("type"))
        {
            case "notify":
                return new SecretaryEvent.Notify(Str("title") ?? "", Str("body") ?? "");
            case "expression":
                if (!SpriteEnumExtensions.TryParsePose(Str("pose"), out var pose)) return null;
                return new SecretaryEvent.Expression(pose, Num("duration") ?? 3);
            case "activity":
                var h = Str("hint");
                foreach (var hint in Enum.GetValues<ActivityHint>())
                    if (JsonNamingPolicy.CamelCase.ConvertName(hint.ToString()) == h) return new SecretaryEvent.ActivityChanged(hint);
                return null;
            case "speak":
                return new SecretaryEvent.Speak(Str("text") ?? "");
            default:
                return null;
        }
    }

    public static string Encode(MascotEvent e)
    {
        var o = new JsonObject();
        switch (e.Kind)
        {
            case MascotEventKind.UserClicked: o["type"] = "click"; break;
            case MascotEventKind.ConversationRequested: o["type"] = "conversation"; break;
            case MascotEventKind.UserMessage: o["type"] = "message"; o["text"] = e.Text ?? ""; break;
        }
        return o.ToJsonString();
    }
}

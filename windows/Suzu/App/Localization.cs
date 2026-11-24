using Suzu.Core;

namespace Suzu.App;

/// <summary>アプリの表示言語。設定画面から切り替えられる（既定は日本語）。</summary>
internal enum AppLanguage { Ja, En }

internal static class LanguageExtensions
{
    public static string Code(this AppLanguage l) => l == AppLanguage.Ja ? "ja" : "en";

    public static AppLanguage FromCode(string? code) => code == "en" ? AppLanguage.En : AppLanguage.Ja;

    /// <summary>選択肢としての表示名（どちらの言語でも読めるよう自言語で表記）</summary>
    public static string DisplayName(this AppLanguage l) => l == AppLanguage.Ja ? "日本語" : "English";

    /// <summary>日本語と英語の文言から、現在の言語のものを選ぶ</summary>
    public static string Pick(this AppLanguage l, string ja, string en) => l == AppLanguage.Ja ? ja : en;

    /// <summary>鈴の頭上に出す記号の表示文字列</summary>
    public static string? EmoteText(this AppLanguage l, Emote e) => e switch
    {
        Emote.None => null,
        Emote.Stretch => l.Pick("ん〜", "Mmm~"),
        _ => e.RawText(),
    };

    /// <summary>コントロールパネルに出す「いまの様子」</summary>
    public static string ActivityText(this AppLanguage l, Activity a) => a switch
    {
        Activity.Idle => l.Pick("のんびり立っている", "Standing around"),
        Activity.Walk => l.Pick("お散歩中", "Taking a walk"),
        Activity.LookAround => l.Pick("きょろきょろしている", "Looking around"),
        Activity.TurnAway => l.Pick("あっちを見ている", "Looking the other way"),
        Activity.Doze => l.Pick("うとうとしている", "Dozing off"),
        Activity.Sit => l.Pick("座っている", "Sitting"),
        Activity.Sleep => l.Pick("眠っている", "Sleeping"),
        Activity.Stretch => l.Pick("伸びをしている", "Stretching"),
        Activity.Joy => l.Pick("ごきげん", "In a good mood"),
        Activity.GoToEdge => l.Pick("画面の端へ向かっている", "Heading to the screen edge"),
        Activity.CrossScreen => l.Pick("隣の画面へお出かけ中", "Heading over to the other screen"),
        Activity.PlayBall => l.Pick("ボールで遊んでいる", "Playing with a ball"),
        Activity.JumpDown => l.Pick("飛び降りようとしている", "About to jump down"),
        Activity.Climb => l.Pick("よじ登っている", "Climbing"),
        Activity.Hang => l.Pick("ぶら下がっている", "Hanging"),
        Activity.React => l.Pick("反応している", "Reacting"),
        Activity.Dragged => l.Pick("つままれている", "Being carried"),
        Activity.Falling => l.Pick("落ちている", "Falling"),
        _ => "",
    };
}

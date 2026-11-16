using System.Globalization;
using System.Text.Json;
using Baytak.Mcp.Common;
using Baytak.Mcp.Mcp;

namespace Baytak.Mcp.WhatsApp;

/// <summary>
/// Every text the assistant sends that the model did not write: the fixed notices, and the wording of a change the person
/// is asked to confirm and of how it ended. These are built from the facts, never by the model, so what stands beside the
/// Confirm button is exactly what will be done. English and Arabic.
/// </summary>
public static class BotTexts
{
    public const string English = "en";
    public const string Arabic = "ar";

    // ==================================================================== language

    /// <summary>Arabic when the text has Arabic letters, English when it has Latin ones, otherwise what it was before ("5", an emoji).</summary>
    public static string Detect(string text, string before)
    {
        if (text.Any(c => c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ')) return Arabic;
        return text.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') ? English : before;
    }

    private static readonly HashSet<string> YesWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "y", "yeah", "yep", "ok", "okay", "confirm", "confirmed", "sure", "go ahead", "do it", "please do", "yes please",
        "yalla", "eh", "aywa", "akid", "tamem", "tamam", "mwefa2", "mwafa2",
        "نعم", "اي", "ايه", "ايوا", "ايوه", "اكيد", "تمام", "موافق", "اوكي", "اوك", "تاكيد", "اكد", "يلا", "ماشي",
    };

    private static readonly HashSet<string> NoWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "n", "nope", "cancel", "stop", "dont", "no thanks", "la", "la2", "laa", "la2a",
        "لا", "الغاء", "الغي", "كلا", "توقف", "لا شكرا",
    };

    /// <summary>True for a plain yes, false for a plain no, null for anything else. Used only while a Confirm button is waiting.</summary>
    public static bool? YesOrNo(string text)
    {
        // Letters, digits and single spaces only; the forms of alef are read as one letter.
        var kept = new string(text.Select(c => c is 'أ' or 'إ' or 'آ' ? 'ا' : c).Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
        var word = string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return YesWords.Contains(word) ? true : NoWords.Contains(word) ? false : null;
    }

    // ==================================================================== fixed notices

    public static string Introduce(string lang, string appUrl) => lang == Arabic
        ? $"مرحباً، أنا مساعد بيتك على واتساب.\nهذا الرقم غير مرتبط بحساب في بيتك بعد. نزّل تطبيق بيتك، سجّل الدخول بهذا الرقم وانضم إلى بنايتك، ثم راسلني من جديد.\n{appUrl}"
        : $"Hi, I'm Baytak's assistant on WhatsApp.\nThis number isn't linked to a Baytak account yet. Get the Baytak app, sign in with this phone number and join your building, then write to me again.\n{appUrl}";

    public static string TextOnly(string lang) => lang == Arabic
        ? "أستطيع قراءة الرسائل المكتوبة فقط حالياً. اكتب لي ما تحتاجه."
        : "I can only read written messages for now. Type what you need.";

    public static string TooMany(string lang) => lang == Arabic
        ? "وصلتني رسائل كثيرة من هذا الرقم. الرجاء المحاولة بعد قليل."
        : "I've had a lot of messages from this number. Please try again in a little while.";

    public static string Trouble(string lang) => lang == Arabic
        ? "حدث خطأ من جهتي. الرجاء المحاولة بعد قليل."
        : "Something went wrong on my side. Please try again in a moment.";

    public static string Unreachable(string lang) => lang == Arabic
        ? "تعذّر الوصول إلى بيتك الآن. الرجاء المحاولة بعد قليل."
        : "I couldn't reach Baytak right now. Please try again in a moment.";

    public static string CannotHelp(string lang) => lang == Arabic
        ? "لا أستطيع المساعدة في هذا. اسألني عن رسوم بنايتك وإيصالاتك."
        : "I can't help with that. Ask me about your building's fees and receipts.";

    public static string Cancelled(string lang) => lang == Arabic ? "تم الإلغاء. لم يتغيّر شيء." : "Cancelled. Nothing was changed.";

    public static string Expired(string lang) => lang == Arabic
        ? "انتهت مهلة هذا الطلب ولم يتغيّر شيء. أخبرني من جديد بما تريد."
        : "That request has expired and nothing was changed. Tell me again what you'd like to do.";

    public static string ConfirmShort(string lang) => lang == Arabic ? "هل تؤكّد؟" : "Do you confirm?";

    public const string ConfirmPrefix = "ok:";
    public const string CancelPrefix = "no:";

    public static IReadOnlyList<ReplyButton> Buttons(string lang, string id) =>
    [
        new ReplyButton(ConfirmPrefix + id, lang == Arabic ? "تأكيد" : "Confirm"),
        new ReplyButton(CancelPrefix + id, lang == Arabic ? "إلغاء" : "Cancel"),
    ];

    // ==================================================================== a change, before and after

    /// <summary>The question shown beside the Confirm and Cancel buttons, from a tool's preview.</summary>
    public static string Confirmation(string lang, string tool, ToolResult preview)
    {
        var d = Data(preview);
        if (tool == "issue_receipt") return IssuePreview(lang, d);
        if (lang != Arabic) return AsQuestion(preview.Text);

        var perApartment = $"{Ltr(BaytakTools.Money(Dec(d, "total"), Str(d, "currency")))} لكل شقة";
        return tool switch
        {
            "set_payment_status" =>
                $"تعليم {ApartmentsAr(Strings(d, "apartments"))} كـ{(Str(d, "status") == "Paid" ? "مدفوعة" : "غير مدفوعة")} في إيصال {DateAr(Str(d, "date"))} ({perApartment})؟",
            "send_payment_reminder" =>
                $"إرسال تذكير بالدفع لإيصال {DateAr(Str(d, "date"))} ({perApartment}) إلى الشقق التي لم تدفع بعد، وعددها {Int(d, "unpaid")}؟",
            "approve_join_request" => $"الموافقة على {JoinRequestAr(d)}؟",
            "reject_join_request" => $"رفض {JoinRequestAr(d)}؟",
            _ => AsQuestion(preview.Text),
        };
    }

    /// <summary>What the person is told once a confirmed change was carried out, or why it was not.</summary>
    public static string Outcome(string lang, string tool, ToolResult result)
    {
        if (result.IsError) return lang == Arabic ? $"لم تتم العملية: {result.Text}" : $"That didn't go through: {result.Text}";
        if (lang != Arabic) return result.Text;

        var d = Data(result);
        switch (tool)
        {
            case "set_payment_status":
                return $"تم تعليم {ApartmentsAr(Strings(d, "apartments"))} كـ{(Str(d, "status") == "Paid" ? "مدفوعة" : "غير مدفوعة")}.";
            case "send_payment_reminder":
                return $"تم إرسال تذكير بالدفع إلى الشقق التي لم تدفع بعد، وعددها {Int(d, "reminded")}.";
            case "approve_join_request":
                return "تمت الموافقة على طلب الانضمام.";
            case "reject_join_request":
                return "تم رفض طلب الانضمام.";
            case "issue_receipt":
                var receipts = Receipts(d);
                return receipts.Count == 1
                    ? $"تم إصدار الإيصال ({Ltr(BaytakTools.Money(Dec(receipts[0], "total"), Str(receipts[0], "currency")))} لكل شقة) وإشعار السكان."
                    : $"تم إصدار {receipts.Count} إيصالات وإشعار السكان.";
            default:
                return result.Text;
        }
    }

    private static string IssuePreview(string lang, JsonElement d)
    {
        var receipts = Receipts(d);
        var arabic = lang == Arabic;

        string Amount(JsonElement r) => arabic
            ? $"{Ltr(BaytakTools.Money(Dec(r, "total"), Str(r, "currency")))} لكل شقة"
            : $"{BaytakTools.Money(Dec(r, "total"), Str(r, "currency"))} per apartment";
        string Date(JsonElement r) => arabic ? DateAr(Str(r, "date")) : Str(r, "date") == "today" ? "today" : BaytakTools.FormatDate(Str(r, "date"));
        string Lines(JsonElement r) => arabic ? Ltr(string.Join(", ", Strings(r, "lines"))) : string.Join(", ", Strings(r, "lines"));
        string Who(JsonElement r)
        {
            var apartments = Strings(r, "apartments");
            if (r.TryGetProperty("allApartments", out var all) && all.ValueKind == JsonValueKind.True)
                return arabic ? $"كل الشقق المعتمدة ({Int(r, "apartmentCount")})" : $"all {Int(r, "apartmentCount")} approved {(Int(r, "apartmentCount") == 1 ? "apartment" : "apartments")}";
            return arabic ? ApartmentsAr(apartments) : $"{(apartments.Count == 1 ? "apartment" : "apartments")} {BaytakTools.Join(apartments)}";
        }

        if (receipts.Count == 1)
        {
            var r = receipts[0];
            return arabic
                ? $"إصدار هذا الإيصال؟\nالمبلغ: {Amount(r)}\nالتاريخ: {Date(r)}\nالبنود: {Lines(r)}\nإلى: {Who(r)}\n\nلا يمكن تعديله أو حذفه بعد الإصدار، وسيصل إشعار إلى السكان."
                : $"Issue this receipt?\nAmount: {Amount(r)}\nDate: {Date(r)}\nLines: {Lines(r)}\nFor: {Who(r)}\n\nIt can't be edited or deleted afterwards, and the residents are notified.";
        }

        var blocks = receipts.Select((r, i) => arabic
            ? $"{i + 1}. {Amount(r)}، {Date(r)}\n{Lines(r)}\nإلى: {Who(r)}"
            : $"{i + 1}. {Amount(r)}, {Date(r)}\n{Lines(r)}\nFor: {Who(r)}");
        return arabic
            ? $"إصدار هذه الإيصالات ({receipts.Count})؟\n\n{string.Join("\n\n", blocks)}\n\nلا يمكن تعديلها أو حذفها بعد الإصدار، وسيصل إشعار إلى السكان."
            : $"Issue these {receipts.Count} receipts?\n\n{string.Join("\n\n", blocks)}\n\nThey can't be edited or deleted afterwards, and the residents are notified.";
    }

    // ==================================================================== helpers

    /// <summary>One receipt is described at the top level, several under "receipts": both read as a list.</summary>
    private static List<JsonElement> Receipts(JsonElement d) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty("receipts", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [d];

    private static string JoinRequestAr(JsonElement d)
    {
        var resident = Str(d, "resident");
        return $"طلب الانضمام للشقة {Ltr(Str(d, "apartment"))}، الطابق {Int(d, "floor")}{(resident.Length == 0 ? "" : $"، من {resident}")}";
    }

    private static string ApartmentsAr(List<string> numbers) =>
        $"{(numbers.Count == 1 ? "الشقة" : "الشقق")} {string.Join(" و", numbers.Select(Ltr))}";

    private static string DateAr(string date) => date is "" or "today" ? "اليوم" : Ltr(date.Length >= 10 ? date[..10] : date);

    private static string AsQuestion(string sentence) => sentence.TrimEnd().TrimEnd('.') + "?";

    /// <summary>Keeps a left-to-right run (a number, "3B", "150 USD") in one piece inside right-to-left text.</summary>
    private static string Ltr(string text) => text.Length == 0 ? text : "⁦" + text + "⁩";

    private static JsonElement Data(ToolResult result) =>
        result.Structured is null ? default : JsonSerializer.SerializeToElement(result.Structured, Http.Json);

    private static string Str(JsonElement d, string name) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement d, string name) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static decimal Dec(JsonElement d, string name) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var m) ? m : 0;

    private static List<string> Strings(JsonElement d, string name) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? "").ToList()
            : [];

    /// <summary>Today in Beirut, for "this month" and "today" in the model's instructions.</summary>
    public static string Today(DateTimeOffset now)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.CreateCustomTimeZone("Beirut", TimeSpan.FromHours(2), "Beirut", "Beirut");
        }
        return TimeZoneInfo.ConvertTime(now, zone).ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture);
    }
}

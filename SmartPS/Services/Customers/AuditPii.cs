using System.Text.RegularExpressions;

namespace SmartPS.Services.Customers;

/// <summary>Che dữ liệu cá nhân trước khi ghi vào nhật ký kiểm toán bất biến.</summary>
public static class AuditPii
{
    private const int VisibleDigits = 3;

    /// <summary>"0988123456" thành "*******456"; không quá 3 ký tự thì che toàn bộ; rỗng thì trả về rỗng.</summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var value = phone.Trim();
        if (value.Length <= VisibleDigits)
        {
            return new string('*', value.Length);
        }

        return new string('*', value.Length - VisibleDigits) + value[^VisibleDigits..];
    }

    /// <summary>Che các dãy từ 9 chữ số trở lên (số điện thoại gõ nhầm vào ô tên) trong một đoạn văn bản.</summary>
    public static string MaskPhoneNumbersInText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Dãy từ 9 chữ số trở lên, cho phép dấu cách, chấm hoặc gạch ngang giữa các chữ số; giữ lại 3 chữ số cuối.
        return Regex.Replace(text, @"(?:\d[\s.\-/_()]{0,2}){9,}", m =>
        {
            var digitsLeft = m.Value.Count(char.IsDigit) - VisibleDigits;
            var chars = m.Value.Select(c =>
            {
                if (!char.IsDigit(c))
                {
                    return c;
                }

                return digitsLeft-- > 0 ? '*' : c;
            });
            return new string(chars.ToArray());
        });
    }
}

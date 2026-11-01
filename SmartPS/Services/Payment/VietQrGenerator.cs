using System.Globalization;
using System.Text;

namespace SmartPS.Services.Payment;

/// <summary>
/// Trình sinh mã chuỗi VietQR chuẩn EMVCo (Napas 247) kèm kiểm tra CRC16-CCITT và link QuickLink tiện ích.
/// Tương thích với tất cả ứng dụng Mobile Banking tại Việt Nam (MBBank, Vietcombank, Techcombank, Momo...).
/// </summary>
public static class VietQrGenerator
{
    // Cấu hình ngân hàng mặc định cho môi trường demo / kiểm thử
    public const string DefaultBankBin = "970422";          // MBBank (Ngân hàng Quân Đội)
    public const string DefaultAccountNumber = "0987654321"; // Số tài khoản thụ hưởng demo
    public const string DefaultAccountName = "SMARTPS PARKING CORP";
    public const string DefaultBankName = "MBBank (Ngân hàng Quân Đội)";

    /// <summary>
    /// Tạo đường link QuickLink chuẩn VietQR có thể quét bằng mọi camera, Zalo hoặc app ngân hàng.
    /// </summary>
    public static string CreateQuickLinkUrl(
        string bankBin,
        string accountNumber,
        decimal amount,
        string reference,
        string accountName = DefaultAccountName)
    {
        var bin = string.IsNullOrWhiteSpace(bankBin) ? DefaultBankBin : bankBin;
        var acc = string.IsNullOrWhiteSpace(accountNumber) ? DefaultAccountNumber : accountNumber;
        var cleanRef = Uri.EscapeDataString(SanitizeAscii(reference));
        var cleanName = Uri.EscapeDataString(SanitizeAscii(accountName));
        var amountLong = (long)Math.Round(amount, 0);

        return $"https://img.vietqr.io/image/{bin}-{acc}-compact2.png?amount={amountLong}&addInfo={cleanRef}&accountName={cleanName}";
    }

    /// <summary>
    /// Sinh chuỗi payload EMVCo đầy đủ các trường dữ liệu theo chuẩn VietQR NAPAS.
    /// </summary>
    public static string CreatePayload(
        string bankBin,
        string accountNumber,
        decimal amount,
        string reference,
        string serviceCode = "QRIBFTTA")
    {
        var bin = string.IsNullOrWhiteSpace(bankBin) ? DefaultBankBin : bankBin;
        var acc = string.IsNullOrWhiteSpace(accountNumber) ? DefaultAccountNumber : accountNumber;

        var sb = new StringBuilder();

        // Tag 00: Payload Format Indicator = 01
        sb.Append(FormatTlv("00", "01"));

        // Tag 01: Point of Initiation Method: 12 (Dynamic QR có số tiền), 11 (Static QR)
        sb.Append(FormatTlv("01", amount > 0 ? "12" : "11"));

        // Tag 38: Merchant Account Information (VietQR NAPAS)
        var subOrg = new StringBuilder();
        subOrg.Append(FormatTlv("00", bin));
        subOrg.Append(FormatTlv("01", acc));

        var sub38 = new StringBuilder();
        sub38.Append(FormatTlv("00", "A000000727")); // NAPAS AID
        sub38.Append(FormatTlv("01", subOrg.ToString()));
        sub38.Append(FormatTlv("02", serviceCode)); // QRIBFTTA (Chuyển nhanh Napas247 đến tài khoản)
        sb.Append(FormatTlv("38", sub38.ToString()));

        // Tag 53: Transaction Currency: 704 (VND)
        sb.Append(FormatTlv("53", "704"));

        // Tag 54: Transaction Amount
        if (amount > 0)
        {
            var amountStr = ((long)Math.Round(amount, 0)).ToString(CultureInfo.InvariantCulture);
            sb.Append(FormatTlv("54", amountStr));
        }

        // Tag 58: Country Code: VN
        sb.Append(FormatTlv("58", "VN"));

        // Tag 59: Merchant Name (Tên chủ tài khoản thụ hưởng)
        var cleanName = SanitizeAscii(DefaultAccountName);
        if (!string.IsNullOrWhiteSpace(cleanName))
        {
            if (cleanName.Length > 25) cleanName = cleanName[..25];
            sb.Append(FormatTlv("59", cleanName));
        }

        // Tag 60: Merchant City
        sb.Append(FormatTlv("60", "HA NOI"));

        // Tag 62: Additional Data Field (Nội dung chuyển khoản chuẩn VietQR Tag 08)
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var cleanRef = SanitizeAscii(reference);
            if (cleanRef.Length > 25) cleanRef = cleanRef[..25];
            var sub62 = new StringBuilder();
            sub62.Append(FormatTlv("08", cleanRef)); // Purpose of transaction (nội dung chuyển khoản)
            sb.Append(FormatTlv("62", sub62.ToString()));
        }

        // Tag 63: CRC16-CCITT (Checksum 4 ký tự hex viết hoa)
        sb.Append("6304");
        var crc = ComputeCrc16(sb.ToString());
        sb.Append(crc);

        return sb.ToString();
    }

    private static string FormatTlv(string tag, string value)
    {
        var len = Encoding.UTF8.GetByteCount(value);
        return $"{tag}{len:D2}{value}";
    }

    /// <summary>
    /// Tính toán mã kiểm tra CRC-16/CCITT (Đa thức 0x1021, Giá trị khởi tạo 0xFFFF).
    /// </summary>
    public static string ComputeCrc16(string data)
    {
        ushort crc = 0xFFFF;
        var bytes = Encoding.ASCII.GetBytes(data);
        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        return crc.ToString("X4");
    }

    private static string SanitizeAscii(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                if (ch <= 127 && !char.IsControl(ch))
                {
                    sb.Append(ch);
                }
            }
        }
        return sb.ToString().Trim();
    }
}

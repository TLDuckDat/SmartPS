using SmartPS.Models.Reports;

namespace SmartPS.Services.Reports;

public static class ReportFileNames
{
    public static string Default(ReportDateRange range)
        => "BaoCao_SmartPS_" + range.Key + ".xlsx";
}

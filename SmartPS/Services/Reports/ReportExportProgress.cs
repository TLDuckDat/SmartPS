// Trạng thái tiến độ của luồng xuất báo cáo Excel.

namespace SmartPS.Services.Reports;

public enum ReportExportPhase
{
    Preparing = 0,
    LoadingData = 1,
    WritingWorkbook = 2,
    Finalizing = 3,
    Completed = 4
}

public sealed record ReportExportProgress(int Percent, ReportExportPhase Phase);

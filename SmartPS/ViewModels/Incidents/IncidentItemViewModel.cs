namespace SmartPS.ViewModels.Incidents;

public class IncidentItemViewModel : ViewModelBase
{
    public int IncidentId { get; set; }
    public string IncidentCode { get; set; } = string.Empty;
    public string IncidentType { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public DateTime ReportedTime { get; set; }
    public string ReportedBy { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    
    private string _status = "Đang xử lý";
    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsResolved));
            }
        }
    }

    public string? ResolutionNotes { get; set; }
    public bool IsResolved => Status == "Đã giải quyết";
}

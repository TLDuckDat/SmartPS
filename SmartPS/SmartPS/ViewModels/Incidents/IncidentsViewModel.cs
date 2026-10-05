using System.Collections.ObjectModel;

namespace SmartPS.ViewModels.Incidents;

public class IncidentsViewModel : ViewModelBase
{
    private readonly List<IncidentItemViewModel> _allIncidents = new();

    private int _totalIncidents;
    public int TotalIncidents
    {
        get => _totalIncidents;
        set => SetProperty(ref _totalIncidents, value);
    }

    private int _pendingIncidents;
    public int PendingIncidents
    {
        get => _pendingIncidents;
        set => SetProperty(ref _pendingIncidents, value);
    }

    private int _resolvedIncidents;
    public int ResolvedIncidents
    {
        get => _resolvedIncidents;
        set => SetProperty(ref _resolvedIncidents, value);
    }

    private string _searchKeyword = string.Empty;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                ApplyFilter();
            }
        }
    }

    public ObservableCollection<IncidentItemViewModel> FilteredIncidents { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<IncidentItemViewModel> ResolveCommand { get; }

    public IncidentsViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ResolveCommand = new RelayCommand<IncidentItemViewModel>(item =>
        {
            if (item != null)
            {
                item.Status = "Đã giải quyết";
                item.ResolutionNotes = "Đã đối chiếu giấy đăng ký xe và CCCD chính chủ. Cho phép xuất bãi an toàn.";
                _ = LoadDataAsync();
            }
        });

        InitializeIncidents();
        _ = LoadDataAsync();
    }

    private void InitializeIncidents()
    {
        _allIncidents.Clear();
        _allIncidents.AddRange(new[]
        {
            new IncidentItemViewModel
            {
                IncidentId = 1,
                IncidentCode = "SC-2026-001",
                IncidentType = "Mất thẻ xe / Vé lượt",
                LicensePlate = "29G1-678.90",
                ReportedTime = DateTime.UtcNow.AddHours(-3),
                ReportedBy = "Nguyễn Văn Tuấn (Ca 1)",
                Notes = "Khách hàng báo làm rơi vé giấy khi mua sắm. Cần trích xuất ảnh vào để xác minh.",
                Status = "Đang xử lý"
            },
            new IncidentItemViewModel
            {
                IncidentId = 2,
                IncidentCode = "SC-2026-002",
                IncidentType = "Biển số bị che khuất / Bám bùn",
                LicensePlate = "30E-123.45",
                ReportedTime = DateTime.UtcNow.AddHours(-8),
                ReportedBy = "Trần Đình Trọng (Ca 2)",
                Notes = "Camera chỉ nhận diện được 4 số đầu do dính bùn đất. Nhân viên đã nhập tay bổ sung.",
                Status = "Đã giải quyết",
                ResolutionNotes = "Đã vệ sinh biển số và đối soát trùng khớp hình ảnh lúc vào."
            },
            new IncidentItemViewModel
            {
                IncidentId = 3,
                IncidentCode = "SC-2026-003",
                IncidentType = "Xe gửi quá hạn 48 giờ",
                LicensePlate = "29A-345.67",
                ReportedTime = DateTime.UtcNow.AddDays(-2),
                ReportedBy = "Lê Hải Đăng (Ca 3)",
                Notes = "Xe đỗ tại ô B-05 quá 2 ngày chưa xuất bãi. Đã liên hệ số điện thoại đăng ký vé.",
                Status = "Đang xử lý"
            }
        });
    }

    public Task LoadDataAsync()
    {
        TotalIncidents = _allIncidents.Count;
        PendingIncidents = _allIncidents.Count(i => !i.IsResolved);
        ResolvedIncidents = _allIncidents.Count(i => i.IsResolved);

        ApplyFilter();
        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        var kw = SearchKeyword?.Trim().ToUpperInvariant() ?? string.Empty;
        FilteredIncidents.Clear();

        foreach (var item in _allIncidents)
        {
            var match = string.IsNullOrEmpty(kw) ||
                        item.IncidentCode.ToUpperInvariant().Contains(kw) ||
                        item.LicensePlate.ToUpperInvariant().Contains(kw) ||
                        item.IncidentType.ToUpperInvariant().Contains(kw);

            if (match)
            {
                FilteredIncidents.Add(item);
            }
        }
    }
}

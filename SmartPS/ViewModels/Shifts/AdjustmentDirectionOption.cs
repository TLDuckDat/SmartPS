using SmartPS.Models.Shifts;

namespace SmartPS.ViewModels.Shifts;

public class AdjustmentDirectionOption
{
    public AdjustmentDirection Direction { get; init; }
    public string DisplayName { get; set; } = string.Empty;
}

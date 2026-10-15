using SmartPS.Models.GateControl;
using SmartPS.Models.Ocr;
using SmartPS.Models.Parking;
using SmartPS.Services.Audio;
using SmartPS.Services.GateControl;
using SmartPS.Services.OcrLisencePlate;
using SmartPS.ViewModels.GateControl;

namespace SmartPS.Tests.Unit;

/// <summary>
/// Branch 2 fix round 1, H1: after a SUCCESSFUL check-in whose result carries
/// <see cref="GateCheckInResult.TicketVehicleTypeMismatchWarning"/> or <see cref="GateCheckInResult.CustomerLockedWarning"/>,
/// the operator sees the localized warning in the lane status line and in the message shown for the check-in.
/// The fake localization returns the resource key, so the assertions look for the key fragment
/// ("TicketVehicleTypeMismatch" / "CustomerLocked"); the implementer may use an existing or a new key containing it.
/// </summary>
public class GateCheckInWarningMessageTests
{
    private const string Plate = "51F12345";

    private static (GateControlViewModel Vm, FakeDialogService Dialogs) CreateVm(GateCheckInResult scripted)
    {
        var context = new CurrentUserContext();
        context.SetUser(TestUsers.Operator());
        var dialogs = new FakeDialogService();
        var vm = new GateControlViewModel(
            new NullOcr(),
            new ScriptedGate(scripted),
            new NullCamera(),
            dialogs,
            new FakeLocalizationService(),
            new FakeAuthService(context),
            new PermissionService(context),
            new SilentAudio());
        return (vm, dialogs);
    }

    private static GateCheckInResult Admitted(VehicleCategory category, bool mismatch = false, bool locked = false) => new()
    {
        Success = true,
        Message = "ok",
        Category = category,
        RejectReason = CheckInRejectReason.None,
        TicketVehicleTypeMismatchWarning = mismatch,
        CustomerLockedWarning = locked,
        AssignedSlotCode = "A-01",
        Session = new ParkingSession { SessionId = 4242, TicketCode = "TK-TEST-1", LicensePlate = Plate, VehicleTypeId = 2, CheckInTime = DateTime.UtcNow }
    };

    private static async Task CheckInAsync(GateControlViewModel vm, FakeDialogService dialogs)
    {
        vm.InPlateText = Plate;
        vm.ConfirmCheckInCommand.Execute(null);

        // AsyncRelayCommand.Execute is async void; every fake completes synchronously, but wait defensively.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dialogs.Successes.Count + dialogs.Warnings.Count + dialogs.Infos.Count + dialogs.Errors.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static string ShownMessages(FakeDialogService d)
        => string.Join(" | ", d.Successes.Concat(d.Warnings).Concat(d.Infos));

    [Fact]
    public async Task H1_ticket_vehicle_type_mismatch_warning_reaches_the_status_line_and_the_shown_message()
    {
        var (vm, dialogs) = CreateVm(Admitted(VehicleCategory.Visitor, mismatch: true));

        await CheckInAsync(vm, dialogs);

        Assert.Empty(dialogs.Errors);
        Assert.Contains("TicketVehicleTypeMismatch", vm.InStatusMessage, StringComparison.Ordinal);
        Assert.Contains("TicketVehicleTypeMismatch", ShownMessages(dialogs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task H1_mismatch_warning_is_shown_for_a_resident_too()
    {
        var (vm, dialogs) = CreateVm(Admitted(VehicleCategory.Resident, mismatch: true));

        await CheckInAsync(vm, dialogs);

        Assert.Contains("TicketVehicleTypeMismatch", vm.InStatusMessage, StringComparison.Ordinal);
        Assert.Contains("TicketVehicleTypeMismatch", ShownMessages(dialogs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task H1_customer_locked_warning_reaches_the_status_line_and_the_shown_message()
    {
        var (vm, dialogs) = CreateVm(Admitted(VehicleCategory.Visitor, locked: true));

        await CheckInAsync(vm, dialogs);

        Assert.Empty(dialogs.Errors);
        Assert.Contains("CustomerLocked", vm.InStatusMessage, StringComparison.Ordinal);
        Assert.Contains("CustomerLocked", ShownMessages(dialogs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task H1_no_warning_text_when_the_result_has_no_warning()
    {
        var (vm, dialogs) = CreateVm(Admitted(VehicleCategory.Visitor));

        await CheckInAsync(vm, dialogs);

        Assert.Single(dialogs.Successes);
        Assert.DoesNotContain("TicketVehicleTypeMismatch", vm.InStatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerLocked", vm.InStatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("TicketVehicleTypeMismatch", ShownMessages(dialogs), StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerLocked", ShownMessages(dialogs), StringComparison.Ordinal);
    }

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class ScriptedGate : IGateControlService
    {
        private readonly GateCheckInResult _result;

        public ScriptedGate(GateCheckInResult result) => _result = result;

        public Task<List<VehicleType>> GetVehicleTypesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<VehicleType>
            {
                new() { VehicleTypeId = 1, TypeName = "Xe máy", Description = "m" },
                new() { VehicleTypeId = 2, TypeName = "Xe ô tô", Description = "c" }
            });

        public Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, CancellationToken cancellationToken = default) => Task.FromResult<ParkingSlot?>(null);
        public Task<ParkingSlot?> SuggestAvailableSlotAsync(int vehicleTypeId, VehicleCategory category, CancellationToken cancellationToken = default) => Task.FromResult<ParkingSlot?>(null);
        public Task<VehicleClassification> ClassifyVehicleAsync(string licensePlate, CancellationToken cancellationToken = default)
            => Task.FromResult(VehicleClassification.Visitor(LicensePlateNormalizer.Normalize(licensePlate)));
        public Task<MonthlyTicket?> FindActiveMonthlyTicketAsync(string licensePlate, CancellationToken cancellationToken = default) => Task.FromResult<MonthlyTicket?>(null);
        public Task<Vehicle?> FindVehicleOwnerAsync(string licensePlate, CancellationToken cancellationToken = default) => Task.FromResult<Vehicle?>(null);
        public Task<BlacklistedVehicle?> CheckBlacklistAsync(string licensePlate, CancellationToken cancellationToken = default) => Task.FromResult<BlacklistedVehicle?>(null);
        public Task<GateCheckInResult> ProcessCheckInAsync(GateCheckInRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_result);
        public Task<GateCheckOutCalculationResult> CalculateCheckOutAsync(string licensePlate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GateCheckOutResult> CompleteCheckOutAsync(GateCheckOutRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ParkingSession>> GetActiveSessionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<ParkingSession>());
        public Task<List<ParkingSlot>> GetAllSlotsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<ParkingSlot>());
        public Task<List<ParkingSession>> GetAllSessionsHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<ParkingSession>());
        public Task<OverviewKpiData> GetOverviewKpiAsync(CancellationToken cancellationToken = default) => Task.FromResult(new OverviewKpiData());
        public event EventHandler<ParkingSession>? SessionCompleted { add { } remove { } }
        public void NotifySessionCompleted(ParkingSession session) { }
    }

    private sealed class NullOcr : IOcrLicensePlateService
    {
        public bool IsRunning => false;
        public string? EngineExecutablePath => null;
        public Task<bool> EnsureEngineStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<OcrPlateResponse> RecognizePlateAsync(string imagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopEngineAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullCamera : ICameraWatcherService
    {
        public bool IsActive => false;
        public string InCameraDirectory => string.Empty;
        public string OutCameraDirectory => string.Empty;
        public event Action<string>? ImageArrivedAtInLane { add { } remove { } }
        public event Action<string>? ImageArrivedAtOutLane { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class SilentAudio : IAudioAlertService
    {
        public void PlaySuccessAlert() { }
        public void PlayWarningAlert() { }
        public void PlayErrorAlert() { }
    }
}

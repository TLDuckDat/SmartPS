// Đọc, chuẩn hóa, kiểm tra và lưu dữ liệu hộ dân cùng phương tiện từ CSV/XLSX.

using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SmartPS.Data;
using SmartPS.DTOs.Customers;
using SmartPS.Models.Parking;
using SmartPS.Services.Localization;

namespace SmartPS.Services.Customers;

public sealed class CustomerImportService : ICustomerImportService
{
    private static readonly IReadOnlyDictionary<string, string[]> HeaderAliases =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ApartmentCode"] = new[] { "apartmentcode", "macanho", "householdcode", "household" },
            ["FullName"] = new[] { "fullname", "hoten", "tenchuxe", "ownername" },
            ["PhoneNumber"] = new[] { "phonenumber", "phone", "sodienthoai", "sdt" },
            ["Email"] = new[] { "email", "emailaddress" },
            ["IdentityCard"] = new[] { "identitycard", "cccd", "cmnd", "identity" },
            ["LicensePlate"] = new[] { "licenseplate", "bienso", "biensoxe", "plate" },
            ["VehicleType"] = new[] { "vehicletype", "loaixe", "loaiphuongtien" },
            ["Color"] = new[] { "color", "mau", "mauxe" },
            ["Brand"] = new[] { "brand", "hangxe" },
            ["MaxVehicles"] = new[] { "maxvehicles", "sotoxida", "gioihansoxe" },
            ["Notes"] = new[] { "notes", "ghichu" }
        };

    private static readonly string[] RequiredHeaders =
        new[] { "ApartmentCode", "FullName", "PhoneNumber", "LicensePlate", "VehicleType" };

    private readonly IDbContextFactory<SmartPsDbContext> _dbFactory;

    public CustomerImportService(IDbContextFactory<SmartPsDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<CustomerImportPreview> PreviewAsync(
        string filePath,
        IProgress<CustomerImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSupportedFile(filePath);

        var rawRows = await Task.Run(
            () => ReadRows(filePath, progress, cancellationToken),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CustomerImportProgress
        {
            Phase = AppText.Get("Str_Customers_ImportValidating"),
            ProcessedRows = 0,
            TotalRows = rawRows.Count
        });

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var lookup = await LoadLookupAsync(db, cancellationToken);

        return await Task.Run(
            () => BuildPreview(rawRows, lookup, progress, cancellationToken),
            cancellationToken);
    }

    public async Task<CustomerImportSaveResult> SaveAsync(
        CustomerImportPreview preview,
        IProgress<CustomerImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (!preview.CanSave)
        {
            throw new InvalidOperationException(AppText.Get("Msg_Customers_ImportHasErrors"));
        }

        var records = preview.Rows
            .Where(row => !row.IsError && row.Record != null)
            .Select(row => row.Record!)
            .ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var households = await db.Households.ToListAsync(cancellationToken);
        var customers = await db.Customers.ToListAsync(cancellationToken);
        var vehicles = await db.Vehicles.Where(x => x.IsActive).ToListAsync(cancellationToken);
        var vehicleTypes = await db.VehicleTypes.ToListAsync(cancellationToken);
        var settings = await db.ParkingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var defaultMaxVehicles = settings?.DefaultMaxVehiclesPerHousehold is > 0
            ? settings.DefaultMaxVehiclesPerHousehold
            : 2;

        var vehicleTypesByName = vehicleTypes.ToDictionary(x => NormalizeLookup(x.TypeName), StringComparer.Ordinal);
        var householdsByApartment = households.ToDictionary(x => NormalizeLookup(x.ApartmentCode), StringComparer.Ordinal);
        var activeVehiclesByPlate = vehicles.ToDictionary(x => NormalizePlate(x.LicensePlate), StringComparer.Ordinal);
        var householdStates = new Dictionary<string, ImportHouseholdState>(StringComparer.Ordinal);
        var createdHouseholds = 0;
        var createdCustomers = 0;
        var createdVehicles = 0;
        var skippedVehicles = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var apartmentKey = NormalizeLookup(record.ApartmentCode);
            if (!householdsByApartment.TryGetValue(apartmentKey, out var household))
            {
                household = new Household
                {
                    ApartmentCode = record.ApartmentCode,
                    MaxVehicles = record.MaxVehicles ?? defaultMaxVehicles,
                    Notes = record.Notes,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                db.Households.Add(household);
                households.Add(household);
                householdsByApartment[apartmentKey] = household;
                createdHouseholds++;
            }

            if (!householdStates.TryGetValue(apartmentKey, out var state))
            {
                var maxVehicles = household.MaxVehicles ?? records.First(x =>
                    string.Equals(NormalizeLookup(x.ApartmentCode), apartmentKey, StringComparison.Ordinal)).MaxVehicles ?? defaultMaxVehicles;
                if (!household.MaxVehicles.HasValue && maxVehicles != defaultMaxVehicles)
                {
                    household.MaxVehicles = maxVehicles;
                }

                state = new ImportHouseholdState(household, CountActiveVehicles(household, customers, vehicles), maxVehicles);
                householdStates[apartmentKey] = state;
            }

            var customer = FindCustomer(record, household, customers);
            if (customer != null && !IsCustomerInHousehold(customer, household))
            {
                throw new InvalidOperationException(AppText.Get("Msg_Customers_ImportConcurrentCustomer", record.RowNumber));
            }

            if (customer == null)
            {
                customer = new Customer
                {
                    FullName = record.FullName,
                    PhoneNumber = record.PhoneNumber,
                    Email = record.Email,
                    IdentityCard = record.IdentityCard,
                    Type = CustomerType.Resident,
                    Household = household,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                db.Customers.Add(customer);
                customers.Add(customer);
                createdCustomers++;
            }

            var plateKey = NormalizePlate(record.LicensePlate);
            if (activeVehiclesByPlate.TryGetValue(plateKey, out var existingVehicle))
            {
                if (existingVehicle.OwnerCustomerId != customer.CustomerId)
                {
                    throw new InvalidOperationException(AppText.Get("Msg_Customers_ImportConcurrentPlate", record.RowNumber, record.LicensePlate));
                }

                skippedVehicles++;
                continue;
            }

            if (state.CurrentVehicleCount >= state.MaxVehicles)
            {
                throw new InvalidOperationException(AppText.Get("Msg_Customers_ImportLimitChanged", record.RowNumber, record.ApartmentCode, state.MaxVehicles));
            }

            if (!vehicleTypesByName.TryGetValue(NormalizeLookup(record.VehicleTypeName), out var vehicleType))
            {
                throw new InvalidOperationException(AppText.Get("Msg_Customers_ImportVehicleTypeMissing", record.RowNumber, record.VehicleTypeName));
            }

            var vehicle = new Vehicle
            {
                LicensePlate = plateKey,
                VehicleTypeId = vehicleType.VehicleTypeId,
                VehicleType = vehicleType,
                Color = record.Color,
                Brand = record.Brand,
                OwnerCustomer = customer,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Vehicles.Add(vehicle);
            vehicles.Add(vehicle);
            activeVehiclesByPlate[plateKey] = vehicle;
            state.CurrentVehicleCount++;
            createdVehicles++;

            progress?.Report(new CustomerImportProgress
            {
                Phase = AppText.Get("Str_Customers_ImportSaving"),
                ProcessedRows = createdVehicles + skippedVehicles,
                TotalRows = records.Count
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        progress?.Report(new CustomerImportProgress
        {
            Phase = AppText.Get("Str_Customers_ImportCompleted"),
            ProcessedRows = records.Count,
            TotalRows = records.Count
        });

        return new CustomerImportSaveResult
        {
            CreatedHouseholds = createdHouseholds,
            CreatedCustomers = createdCustomers,
            CreatedVehicles = createdVehicles,
            SkippedVehicles = skippedVehicles
        };
    }

    private async Task<ImportLookup> LoadLookupAsync(SmartPsDbContext db, CancellationToken cancellationToken)
    {
        var households = await db.Households.AsNoTracking().ToListAsync(cancellationToken);
        var customers = await db.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var vehicleTypes = await db.VehicleTypes.AsNoTracking().ToListAsync(cancellationToken);
        var settings = await db.ParkingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        return new ImportLookup(
            households,
            customers,
            vehicles,
            vehicleTypes,
            settings?.DefaultMaxVehiclesPerHousehold is > 0 ? settings.DefaultMaxVehiclesPerHousehold : 2);
    }

    private static CustomerImportPreview BuildPreview(
        IReadOnlyList<RawImportRow> rawRows,
        ImportLookup lookup,
        IProgress<CustomerImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var parsed = new List<ParsedImportRow>(rawRows.Count);
        var vehicleTypesByName = lookup.VehicleTypes
            .ToDictionary(x => NormalizeLookup(x.TypeName), StringComparer.Ordinal);

        for (var index = 0; index < rawRows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = rawRows[index];
            var errors = new List<string>();
            var apartmentCode = Clean(raw.Get("ApartmentCode"));
            var fullName = Clean(raw.Get("FullName"));
            var phoneNumber = Clean(raw.Get("PhoneNumber"));
            var email = CleanOptional(raw.Get("Email"));
            var identityCard = CleanOptional(raw.Get("IdentityCard"));
            var licensePlate = NormalizePlate(raw.Get("LicensePlate"));
            var vehicleTypeName = Clean(raw.Get("VehicleType"));
            var color = CleanOptional(raw.Get("Color"));
            var brand = CleanOptional(raw.Get("Brand"));
            var notes = CleanOptional(raw.Get("Notes"));
            var maxVehicles = ParseMaxVehicles(raw.Get("MaxVehicles"), errors);

            AddRequiredError(errors, apartmentCode, "Mã căn hộ");
            AddRequiredError(errors, fullName, "Họ tên");
            AddRequiredError(errors, phoneNumber, "Số điện thoại");
            AddRequiredError(errors, licensePlate, "Biển số");
            AddRequiredError(errors, vehicleTypeName, "Loại phương tiện");

            AddLengthError(errors, apartmentCode, 20, "Mã căn hộ");
            AddLengthError(errors, fullName, 100, "Họ tên");
            AddLengthError(errors, phoneNumber, 20, "Số điện thoại");
            AddLengthError(errors, email, 100, "Email");
            AddLengthError(errors, identityCard, 30, "CCCD/CMND");
            AddLengthError(errors, licensePlate, 20, "Biển số");
            AddLengthError(errors, color, 50, "Màu xe");
            AddLengthError(errors, brand, 50, "Hãng xe");
            AddLengthError(errors, notes, 500, "Ghi chú");

            var vehicleTypeId = 0;
            if (!string.IsNullOrWhiteSpace(vehicleTypeName))
            {
                if (vehicleTypesByName.TryGetValue(NormalizeLookup(vehicleTypeName), out var vehicleType))
                {
                    vehicleTypeId = vehicleType.VehicleTypeId;
                    vehicleTypeName = vehicleType.TypeName;
                }
                else
                {
                    errors.Add(AppText.Get("Msg_Customers_ImportVehicleTypeMissing", raw.RowNumber, vehicleTypeName));
                }
            }

            var record = new CustomerImportRecord(
                raw.RowNumber,
                apartmentCode,
                fullName,
                phoneNumber,
                email,
                identityCard,
                licensePlate,
                vehicleTypeName,
                vehicleTypeId,
                color,
                brand,
                maxVehicles,
                notes);

            parsed.Add(new ParsedImportRow(raw.RowNumber, record, errors));
            progress?.Report(new CustomerImportProgress
            {
                Phase = AppText.Get("Str_Customers_ImportValidating"),
                ProcessedRows = index + 1,
                TotalRows = rawRows.Count
            });
        }

        ValidateDuplicates(parsed);
        ValidateExistingData(parsed, lookup);
        ValidateHouseholdLimits(parsed, lookup);

        var rows = parsed.Select(ToResult).ToList();
        return new CustomerImportPreview
        {
            Rows = rows,
            Records = rows.Where(x => x.Record != null).Select(x => x.Record!).ToList()
        };
    }

    private static void ValidateDuplicates(IReadOnlyList<ParsedImportRow> rows)
    {
        var plates = new Dictionary<string, int>(StringComparer.Ordinal);
        var customerKeys = new Dictionary<string, (int RowNumber, string Name, string ApartmentCode)>(StringComparer.Ordinal);
        var maxVehicles = new Dictionary<string, (int RowNumber, int Value)>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var record = row.Record;
            if (record == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(record.LicensePlate))
            {
                if (plates.TryGetValue(record.LicensePlate, out var firstRow))
                {
                    row.Errors.Add(AppText.Get("Msg_Customers_ImportDuplicatePlate", firstRow));
                }
                else
                {
                    plates[record.LicensePlate] = record.RowNumber;
                }
            }

            var customerKey = GetCustomerKey(record);
            if (customerKeys.TryGetValue(customerKey, out var firstCustomer) &&
                (!string.Equals(firstCustomer.Name, record.FullName, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(NormalizeLookup(firstCustomer.ApartmentCode), NormalizeLookup(record.ApartmentCode), StringComparison.Ordinal)))
            {
                row.Errors.Add(AppText.Get("Msg_Customers_ImportCustomerConflict", firstCustomer.RowNumber));
            }
            else
            {
                customerKeys[customerKey] = (record.RowNumber, record.FullName, record.ApartmentCode);
            }

            if (record.MaxVehicles.HasValue)
            {
                var apartmentKey = NormalizeLookup(record.ApartmentCode);
                if (maxVehicles.TryGetValue(apartmentKey, out var firstMax) && firstMax.Value != record.MaxVehicles.Value)
                {
                    row.Errors.Add(AppText.Get("Msg_Customers_ImportMaxConflict", firstMax.RowNumber));
                }
                else
                {
                    maxVehicles[apartmentKey] = (record.RowNumber, record.MaxVehicles.Value);
                }
            }
        }
    }

    private static void ValidateExistingData(IReadOnlyList<ParsedImportRow> rows, ImportLookup lookup)
    {
        foreach (var row in rows)
        {
            var record = row.Record;
            if (record == null)
            {
                continue;
            }

            var household = lookup.Households.FirstOrDefault(x =>
                string.Equals(NormalizeLookup(x.ApartmentCode), NormalizeLookup(record.ApartmentCode), StringComparison.Ordinal));

            if (household != null && record.MaxVehicles.HasValue && household.MaxVehicles.HasValue && household.MaxVehicles.Value != record.MaxVehicles.Value)
            {
                row.Errors.Add(AppText.Get("Msg_Customers_ImportExistingMaxConflict", household.MaxVehicles.Value));
            }

            var existingCustomer = FindCustomer(record, household, lookup.Customers);
            if (existingCustomer != null && (household == null || !IsCustomerInHousehold(existingCustomer, household)))
            {
                row.Errors.Add(AppText.Get("Msg_Customers_ImportExistingCustomerConflict"));
            }
            else if (existingCustomer != null && !string.Equals(existingCustomer.FullName.Trim(), record.FullName, StringComparison.OrdinalIgnoreCase))
            {
                row.Errors.Add(AppText.Get("Msg_Customers_ImportExistingCustomerConflict"));
            }

            var existingVehicle = lookup.Vehicles.FirstOrDefault(x =>
                string.Equals(NormalizePlate(x.LicensePlate), record.LicensePlate, StringComparison.Ordinal));

            if (existingVehicle == null)
            {
                continue;
            }

            if (existingCustomer != null && existingVehicle.OwnerCustomerId == existingCustomer.CustomerId)
            {
                row.Warnings.Add(AppText.Get("Msg_Customers_ImportVehicleExists"));
            }
            else
            {
                row.Errors.Add(AppText.Get("Msg_Customers_ImportPlateOwned"));
            }
        }
    }

    private static void ValidateHouseholdLimits(IReadOnlyList<ParsedImportRow> rows, ImportLookup lookup)
    {
        foreach (var group in rows
                     .Where(x => x.Record != null && x.Errors.Count == 0)
                     .GroupBy(x => NormalizeLookup(x.Record!.ApartmentCode), StringComparer.Ordinal))
        {
            var first = group.First().Record!;
            var household = lookup.Households.FirstOrDefault(x =>
                string.Equals(NormalizeLookup(x.ApartmentCode), group.Key, StringComparison.Ordinal));
            var maxVehicles = household?.MaxVehicles ?? first.MaxVehicles ?? lookup.DefaultMaxVehicles;
            var existingCount = household == null
                ? 0
                : CountActiveVehicles(household, lookup.Customers, lookup.Vehicles);
            var newCount = 0;

            foreach (var row in group)
            {
                var record = row.Record!;
                var existingVehicle = lookup.Vehicles.Any(x =>
                    string.Equals(NormalizePlate(x.LicensePlate), record.LicensePlate, StringComparison.Ordinal));
                if (existingVehicle)
                {
                    continue;
                }

                newCount++;
                if (existingCount + newCount > maxVehicles)
                {
                    row.Errors.Add(AppText.Get("Msg_Customers_ImportHouseholdLimit", maxVehicles));
                }
            }
        }
    }

    private static CustomerImportRowResult ToResult(ParsedImportRow row)
    {
        var status = row.Errors.Count > 0
            ? CustomerImportRowStatus.Error
            : row.Warnings.Count > 0
                ? CustomerImportRowStatus.Warning
                : CustomerImportRowStatus.Valid;

        return new CustomerImportRowResult
        {
            RowNumber = row.RowNumber,
            ApartmentCode = row.Record.ApartmentCode,
            OwnerName = row.Record.FullName,
            LicensePlate = row.Record.LicensePlate,
            VehicleTypeName = row.Record.VehicleTypeName,
            Status = status,
            Message = string.Join("; ", row.Errors.Concat(row.Warnings)),
            Record = row.Record
        };
    }

    private static int? ParseMaxVehicles(string value, ICollection<string> errors)
    {
        var cleaned = CleanOptional(value);
        if (cleaned == null)
        {
            return null;
        }

        if (!int.TryParse(cleaned, out var maxVehicles) || maxVehicles is < 1 or > 20)
        {
            errors.Add(AppText.Get("Msg_Customers_ImportMaxInvalid"));
            return null;
        }

        return maxVehicles;
    }

    private static void AddRequiredError(ICollection<string> errors, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(AppText.Get("Msg_Customers_ImportRequired", fieldName));
        }
    }

    private static void AddLengthError(ICollection<string> errors, string? value, int maxLength, string fieldName)
    {
        if (!string.IsNullOrEmpty(value) && value.Length > maxLength)
        {
            errors.Add(AppText.Get("Msg_Customers_ImportTooLong", fieldName, maxLength));
        }
    }

    private static List<RawImportRow> ReadRows(string filePath, IProgress<CustomerImportProgress>? progress, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadExcelRows(filePath, progress, cancellationToken)
            : ReadCsvRows(filePath, progress, cancellationToken);
    }

    private static List<RawImportRow> ReadExcelRows(string filePath, IProgress<CustomerImportProgress>? progress, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new FormatException(AppText.Get("Msg_Customers_ImportNoSheet"));
        var usedRows = worksheet.RowsUsed().ToList();
        if (usedRows.Count == 0)
        {
            throw new FormatException(AppText.Get("Msg_Customers_ImportEmptyFile"));
        }

        var lastColumn = usedRows.Max(x => x.LastCellUsed()?.Address.ColumnNumber ?? 0);
        var headers = Enumerable.Range(1, lastColumn)
            .Select(index => worksheet.Cell(usedRows[0].RowNumber(), index).GetString())
            .ToList();
        var columns = MapHeaders(headers);
        var rows = new List<RawImportRow>();

        for (var index = 1; index < usedRows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = usedRows[index];
            var values = Enumerable.Range(1, lastColumn)
                .Select(column => row.Cell(column).GetString())
                .ToList();
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(CreateRawRow(row.RowNumber(), values, columns));
            progress?.Report(new CustomerImportProgress
            {
                Phase = AppText.Get("Str_Customers_ImportReading"),
                ProcessedRows = rows.Count,
                TotalRows = Math.Max(usedRows.Count - 1, 1)
            });
        }

        if (rows.Count == 0)
        {
            throw new FormatException(AppText.Get("Msg_Customers_ImportEmptyFile"));
        }

        return rows;
    }

    private static List<RawImportRow> ReadCsvRows(string filePath, IProgress<CustomerImportProgress>? progress, CancellationToken cancellationToken)
    {
        var text = File.ReadAllText(filePath, Encoding.UTF8);
        cancellationToken.ThrowIfCancellationRequested();
        var delimiter = DetectDelimiter(text);
        var records = ParseCsvRecords(text, delimiter, cancellationToken);
        if (records.Count == 0)
        {
            throw new FormatException(AppText.Get("Msg_Customers_ImportEmptyFile"));
        }

        var columns = MapHeaders(records[0]);
        var rows = new List<RawImportRow>();
        for (var index = 1; index < records.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = records[index];
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(CreateRawRow(index + 1, values, columns));
            progress?.Report(new CustomerImportProgress
            {
                Phase = AppText.Get("Str_Customers_ImportReading"),
                ProcessedRows = rows.Count,
                TotalRows = Math.Max(records.Count - 1, 1)
            });
        }

        if (rows.Count == 0)
        {
            throw new FormatException(AppText.Get("Msg_Customers_ImportEmptyFile"));
        }

        return rows;
    }

    private static RawImportRow CreateRawRow(int rowNumber, IReadOnlyList<string> values, IReadOnlyDictionary<string, int> columns)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            data[column.Key] = column.Value < values.Count ? values[column.Value] : string.Empty;
        }

        return new RawImportRow(rowNumber, data);
    }

    private static IReadOnlyDictionary<string, int> MapHeaders(IReadOnlyList<string> headers)
    {
        var normalized = headers
            .Select(NormalizeHeader)
            .ToList();
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var pair in HeaderAliases)
        {
            var index = normalized.FindIndex(value => pair.Value.Contains(value, StringComparer.Ordinal));
            if (index >= 0)
            {
                columns[pair.Key] = index;
            }
        }

        var missing = RequiredHeaders.Where(header => !columns.ContainsKey(header)).ToList();
        if (missing.Count > 0)
        {
            throw new FormatException(AppText.Get("Msg_Customers_ImportMissingHeaders", string.Join(", ", missing.Select(GetHeaderDisplayName))));
        }

        return columns;
    }

    private static List<IReadOnlyList<string>> ParseCsvRecords(string text, char delimiter, CancellationToken cancellationToken)
    {
        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (!quoted && character == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!quoted && character is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                records.Add(fields.ToList());
                fields.Clear();
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            field.Append(character);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields.ToList());
        }

        return records;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        var commaCount = firstLine.Count(x => x == ',');
        var semicolonCount = firstLine.Count(x => x == ';');
        var tabCount = firstLine.Count(x => x == '\t');
        return semicolonCount > commaCount && semicolonCount >= tabCount
            ? ';'
            : tabCount > commaCount
                ? '\t'
                : ',';
    }

    private static string NormalizeHeader(string value)
        => new string(RemoveDiacritics(Clean(value).TrimStart('\uFEFF')).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeLookup(string? value)
        => Clean(value).ToUpperInvariant();

    private static string NormalizePhone(string? value)
        => new string(Clean(value).Where(char.IsDigit).ToArray());

    private static string NormalizeIdentity(string? value)
        => new string(Clean(value).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string NormalizePlate(string? value)
        => Regex.Replace(Clean(value), "[^a-zA-Z0-9]", string.Empty).ToUpperInvariant();

    private static string Clean(string? value)
        => value?.Trim() ?? string.Empty;

    private static string? CleanOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetCustomerKey(CustomerImportRecord record)
        => !string.IsNullOrWhiteSpace(record.IdentityCard)
            ? $"ID:{NormalizeIdentity(record.IdentityCard)}"
            : $"APT:{NormalizeLookup(record.ApartmentCode)}|PHONE:{NormalizePhone(record.PhoneNumber)}";

    private static Customer? FindCustomer(CustomerImportRecord record, Household? household, IReadOnlyList<Customer> customers)
    {
        var identity = NormalizeIdentity(record.IdentityCard);
        if (!string.IsNullOrWhiteSpace(identity))
        {
            var byIdentity = customers.FirstOrDefault(x => NormalizeIdentity(x.IdentityCard) == identity);
            if (byIdentity != null)
            {
                return byIdentity;
            }
        }

        if (household == null)
        {
            return null;
        }

        var phone = NormalizePhone(record.PhoneNumber);
        return customers.FirstOrDefault(x =>
            IsCustomerInHousehold(x, household) &&
            NormalizePhone(x.PhoneNumber) == phone);
    }

    private static bool IsCustomerInHousehold(Customer customer, Household household)
        => household.HouseholdId != 0
            ? customer.HouseholdId == household.HouseholdId
            : ReferenceEquals(customer.Household, household);

    private static int CountActiveVehicles(Household household, IReadOnlyList<Customer> customers, IReadOnlyList<Vehicle> vehicles)
    {
        var customerIds = customers
            .Where(x => x.HouseholdId == household.HouseholdId)
            .Select(x => x.CustomerId)
            .ToHashSet();
        return vehicles.Count(x => x.IsActive && customerIds.Contains(x.OwnerCustomerId));
    }

    private static void EnsureSupportedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException(AppText.Get("Msg_Customers_ImportFileNotFound"), filePath);
        }

        var extension = Path.GetExtension(filePath);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(AppText.Get("Msg_Customers_ImportUnsupportedFile"));
        }
    }

    private static string GetHeaderDisplayName(string key) => key switch
    {
        "ApartmentCode" => "ApartmentCode",
        "FullName" => "FullName",
        "PhoneNumber" => "PhoneNumber",
        "LicensePlate" => "LicensePlate",
        "VehicleType" => "VehicleType",
        _ => key
    };

    private sealed record RawImportRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
    {
        public string Get(string key) => Values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private sealed class ParsedImportRow
    {
        public ParsedImportRow(int rowNumber, CustomerImportRecord record, List<string> errors)
        {
            RowNumber = rowNumber;
            Record = record;
            Errors = errors;
        }

        public int RowNumber { get; }
        public CustomerImportRecord Record { get; }
        public List<string> Errors { get; }
        public List<string> Warnings { get; } = new();
    }

    private sealed record ImportLookup(
        IReadOnlyList<Household> Households,
        IReadOnlyList<Customer> Customers,
        IReadOnlyList<Vehicle> Vehicles,
        IReadOnlyList<VehicleType> VehicleTypes,
        int DefaultMaxVehicles);

    private sealed class ImportHouseholdState
    {
        public ImportHouseholdState(Household household, int currentVehicleCount, int maxVehicles)
        {
            Household = household;
            CurrentVehicleCount = currentVehicleCount;
            MaxVehicles = maxVehicles;
        }

        public Household Household { get; }
        public int CurrentVehicleCount { get; set; }
        public int MaxVehicles { get; }
    }
}

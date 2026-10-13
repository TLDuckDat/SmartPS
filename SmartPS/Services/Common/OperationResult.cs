namespace SmartPS.Services.Common;

/// <summary>Lý do thất bại của một thao tác ghi nghiệp vụ; hiển thị qua khoá Msg_Op_&lt;tên&gt; trong giao diện.</summary>
public enum OperationError
{
    None = 0,
    PermissionDenied,
    Validation,
    NotFound,
    DatabaseError
}

/// <summary>Kết quả của thao tác ghi: không ném ngoại lệ khi bị từ chối quyền hay vi phạm quy tắc nghiệp vụ.</summary>
public class OperationResult
{
    public bool Success { get; init; }

    public OperationError Error { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool IsPermissionDenied => Error == OperationError.PermissionDenied;

    public static OperationResult Ok(string message = "")
        => new() { Success = true, Message = message };

    public static OperationResult Fail(OperationError error, string message)
        => new() { Success = false, Error = error, Message = message };
}

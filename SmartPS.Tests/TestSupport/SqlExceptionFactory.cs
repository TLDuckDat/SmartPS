using System.Reflection;
using Microsoft.Data.SqlClient;

namespace SmartPS.Tests.TestSupport;

/// <summary>
/// Builds a <see cref="SqlException"/> with the given error numbers. SqlException and SqlError have no public constructors,
/// so the internal ones are called through reflection (the same ones SqlClient uses when it reads errors from the server).
/// </summary>
public static class SqlExceptionFactory
{
    public static SqlException Create(int number, string message = "server error", byte errorClass = 16)
        => Create(new[] { (number, message) }, errorClass);

    public static SqlException Create(IEnumerable<(int Number, string Message)> errors, byte errorClass = 16)
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var add = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var (number, message) in errors)
        {
            add.Invoke(collection, new object[] { CreateError(number, message, errorClass) });
        }

        var create = typeof(SqlException).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.Name == "CreateException")
            .Select(m => (Method: m, Parameters: m.GetParameters()))
            .Where(x => x.Parameters.Length >= 2 && x.Parameters[0].ParameterType == typeof(SqlErrorCollection)
                        && x.Parameters[1].ParameterType == typeof(string))
            .OrderBy(x => x.Parameters.Length)
            .First();
        var args = create.Parameters.Select((p, i) => i switch
        {
            0 => collection,
            1 => "16.0.0",
            _ => p.HasDefaultValue ? p.DefaultValue : null
        }).ToArray();
        return (SqlException)create.Method.Invoke(null, args)!;
    }

    private static SqlError CreateError(int number, string message, byte errorClass)
    {
        // Lấy constructor nội bộ đầu tiên có (int number, byte state, byte class, string server, string message, ...)
        var ctor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderBy(c => c.GetParameters().Length)
            .First(c => c.GetParameters() is { Length: >= 5 } p && p[0].ParameterType == typeof(int) && p[1].ParameterType == typeof(byte));

        var args = ctor.GetParameters().Select((p, i) => i switch
        {
            0 => number,
            1 => (byte)1,
            2 => errorClass,
            _ when p.ParameterType == typeof(string) && p.Name is "errorMessage" => message,
            _ when p.ParameterType == typeof(string) => "localhost",
            _ when p.ParameterType == typeof(int) => 0,
            _ when p.ParameterType == typeof(uint) => 0u,
            _ => p.HasDefaultValue ? p.DefaultValue : null
        }).ToArray();
        return (SqlError)ctor.Invoke(args);
    }
}

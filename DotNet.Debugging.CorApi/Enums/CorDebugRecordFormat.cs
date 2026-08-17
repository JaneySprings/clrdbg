namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugrecordformat-enumeration
public enum CorDebugRecordFormat {
    FORMAT_WINDOWS_EXCEPTIONRECORD32 = 1,
    FORMAT_WINDOWS_EXCEPTIONRECORD64
}
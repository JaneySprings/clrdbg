namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corlocalrefpreservation-enumeration
public enum CorLocalRefPreservation {
    MDPreserveLocalRefsNone,
    MDPreserveLocalTypeRef,
    MDPreserveLocalMemberRef
}
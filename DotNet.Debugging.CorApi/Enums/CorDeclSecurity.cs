namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cordeclsecurity-enumeration
public enum CorDeclSecurity {
    dclActionMask = 31,
    dclActionNil = 0,
    dclRequest = 1,
    dclDemand = 2,
    dclAssert = 3,
    dclDeny = 4,
    dclPermitOnly = 5,
    dclLinktimeCheck = 6,
    dclInheritanceCheck = 7,
    dclRequestMinimum = 8,
    dclRequestOptional = 9,
    dclRequestRefuse = 10,
    dclPrejitGrant = 11,
    dclPrejitDenied = 12,
    dclNonCasDemand = 13,
    dclNonCasLinkDemand = 14,
    dclNonCasInheritance = 15,
    dclMaximumValue = dclNonCasInheritance
}
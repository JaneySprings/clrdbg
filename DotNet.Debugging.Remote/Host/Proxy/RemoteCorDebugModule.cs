using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// A module of the app. Its name is the path on the device; when that path does not exist here the file is looked up by
// name in the assemblies folders of the launch, where the build keeps a copy of the app's assemblies, so the debugger
// reads metadata and symbols from its own copy. The metadata importer is the device's, proxied.
// A module the runtime received as bytes rather than a file (Android's loader hands it the core library that way) has
// no path: the runtime names it by its simple name, 'System.Private.CoreLib', and reports it as in memory
// (ICorDebugModule::IsInMemory, dacdbiimpl.cpp GetModuleData: in memory means the assembly has no path; module.cpp
// CordbModule::GetNameWorker: a module without a path is named by DacDbiInterfaceImpl::GetModuleSimpleName). The file
// of that name plus '.dll' in the assemblies folders is the same image, so it is reported as that file, and not in
// memory: the debugger then reads the file instead of copying the image out of the app
[GeneratedComClass]
internal partial class RemoteCorDebugModule : RemoteObject, ICorDebugModule, ICorDebugModule2 {
    private string? localName;
    private bool hasLocalFile;
    private bool? isInMemory;

    public RemoteCorDebugModule(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryGetName(uint cchName, out uint pcchName, char[]? szName) {
        var hr = GetLocalName(out var name);
        if (hr != Cor.S_OK) {
            pcchName = 0;
            return hr;
        }
        pcchName = (uint)name.Length + 1;
        CopyText(name, szName);
        return Cor.S_OK;
    }
    // https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/debugging/icordebugmodule-isinmemory-method
    public int TryIsInMemory(out bool pInMemory) {
        if (isInMemory != null) {
            pInMemory = isInMemory.Value;
            return Cor.S_OK;
        }
        var hr = InvokeBool(Iids.Module, 19, out pInMemory);
        if (hr == Cor.S_OK && pInMemory && GetLocalName(out _) == Cor.S_OK && hasLocalFile)
            pInMemory = false;
        if (hr == Cor.S_OK)
            isInMemory = pInMemory;
        return hr;
    }
    // The importer is the device's; the debugger casts the object to the interface it asked for
    public int TryGetMetaDataInterface(ref Guid riid, out object? ppObj) {
        var importer = RemoteArgument.OutObject();
        var hr = Invoke(Iids.Module, 14, RemoteArgument.Iid(riid), importer);
        ppObj = Session.GetProxy<IMetaDataImport>(importer.HandleResult);
        return hr;
    }

    private int GetLocalName(out string name) {
        if (localName != null) {
            name = localName;
            return Cor.S_OK;
        }
        name = string.Empty;
        var hr = InvokeText(Iids.Module, 6, 0, out var length, null);
        if (hr != Cor.S_OK)
            return hr;
        var buffer = new char[length];
        hr = InvokeText(Iids.Module, 6, length, out _, buffer);
        if (hr != Cor.S_OK)
            return hr;
        var deviceName = new string(buffer).TrimEnd('\0');
        name = deviceName;
        hasLocalFile = File.Exists(deviceName);
        if (!hasLocalFile) {
            var localFile = FindLocalFile(Path.GetFileName(deviceName));
            if (localFile != null) {
                name = localFile;
                hasLocalFile = true;
            }
        }
        localName = name;
        return Cor.S_OK;
    }
    // The file of the device's module in the assemblies folders of the launch; a simple name has no extension, its
    // file has '.dll' behind it (the dots of 'System.Private.CoreLib' are not an extension)
    private string? FindLocalFile(string fileName) {
        var isSimpleName = !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        foreach (var folder in Session.AssembliesPaths) {
            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
                return candidate;
            if (isSimpleName && File.Exists(candidate + ".dll"))
                return candidate + ".dll";
        }
        return null;
    }
}

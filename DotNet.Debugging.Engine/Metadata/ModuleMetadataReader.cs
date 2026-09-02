using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Metadata;

// Reads the metadata of a module and, when available, its portable PDB. The metadata comes from the PE image of
// a module with one, or from a copy of the runtime's metadata for a dynamic (Reflection.Emit) module, which has
// neither an image nor symbols
internal sealed class ModuleMetadataReader : IDisposable {
    // https://github.com/dotnet/roslyn/blob/main/src/Dependencies/CodeAnalysis.Debugging/PortableCustomDebugInfoKinds.cs
    private static readonly Guid asyncMethodSteppingInformationGuid = new Guid("54FD2AC5-E925-401A-9C2A-F94F171072F8");
    private static readonly Guid sourceLinkGuid = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly Guid sha1AlgorithmGuid = new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid sha256AlgorithmGuid = new Guid("8829d00f-11b8-4213-878b-770e8597ac16");
    private const ushort PortableCodeViewVersionMagic = 0x504d;

    private readonly PEReader? peReader;
    // The metadata of a dynamic module. Allocated on the pinned heap, where the GC never moves it, so the reader
    // can address it for as long as the array is referenced - nothing has to be freed when a reader is replaced
    private readonly byte[]? metadataImage;
    private MetadataReaderProvider? pdbProvider;
    private SourceLinkMap? sourceLinkMap;
    private bool sourceLinkMapLoaded;
    private DebugDirectoryEntry codeViewEntry;

    public MetadataReader PeMetadataReader { get; }
    public MetadataReader? PdbMetadataReader { get; private set; }
    // Path of the external portable PDB the symbols were read from, null for embedded or missing symbols
    public string? SymbolFilePath { get; private set; }
    public Guid Mvid { get; }
    public bool HasSymbols => PdbMetadataReader != null;

    private ModuleMetadataReader(PEReader peReader) {
        this.peReader = peReader;
        PeMetadataReader = peReader.GetMetadataReader(MetadataReaderOptions.None);
        Mvid = PeMetadataReader.GetGuid(PeMetadataReader.GetModuleDefinition().Mvid);
    }
    private unsafe ModuleMetadataReader(byte[] metadataImage) {
        this.metadataImage = metadataImage;
        PeMetadataReader = new MetadataReader((byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(metadataImage)), metadataImage.Length, MetadataReaderOptions.None);
        Mvid = PeMetadataReader.GetGuid(PeMetadataReader.GetModuleDefinition().Mvid);
    }

    public static ModuleMetadataReader? TryLoad(string assemblyPath) {
        if (!File.Exists(assemblyPath))
            return null;
        try {
            using var stream = File.OpenRead(assemblyPath);
            return Load(stream, assemblyPath);
        }
        catch {
            return null;
        }
    }
    public static ModuleMetadataReader? TryLoad(byte[] image) {
        try {
            using var stream = new MemoryStream(image, writable: false);
            return Load(stream, null);
        }
        catch {
            return null;
        }
    }
    // The raw metadata of a dynamic module, as the runtime's metadata importer holds it. It is copied: the runtime
    // replaces that buffer every time the module gains a type, so it cannot be addressed beyond this call
    public static unsafe ModuleMetadataReader? TryLoad(nint metadata, int size) {
        if (metadata == 0 || size <= 0)
            return null;
        try {
            var image = GC.AllocateUninitializedArray<byte>(size, pinned: true);
            new ReadOnlySpan<byte>((void*)metadata, size).CopyTo(image);
            return new ModuleMetadataReader(image);
        }
        catch {
            return null;
        }
    }

    // The metadata as one block for the expression compiler, addressable for as long as this reader is
    public unsafe (nint Pointer, int Size) GetMetadataStorage() {
        return ((nint)PeMetadataReader.MetadataPointer, PeMetadataReader.MetadataLength);
    }

    public Version? GetAssemblyVersion() {
        try {
            return PeMetadataReader.IsAssembly ? PeMetadataReader.GetAssemblyDefinition().Version : null;
        }
        catch {
            return null;
        }
    }
    // The managed entry point (MethodDef token) of the assembly, null when it has none (libraries, native entry points)
    public int? GetEntryPointToken() {
        var corHeader = peReader?.PEHeaders.CorHeader;
        if (corHeader == null || (corHeader.Flags & CorFlags.NativeEntryPoint) != 0)
            return null;

        var entryPointToken = corHeader.EntryPointTokenOrRelativeVirtualAddress;
        if (entryPointToken >> 24 != 0x06)
            return null;
        return entryPointToken;
    }

    // Methods without any sequence points, like the compiler's '<Main>' bridge over an async Main - Just
    // My Code treats them as non-user code, the way Microsoft's debugger does
    public IEnumerable<int> GetMethodsWithoutSequencePoints() {
        var reader = PdbMetadataReader;
        if (reader == null)
            yield break;
        foreach (var handle in reader.MethodDebugInformation) {
            if (reader.GetMethodDebugInformation(handle).SequencePointsBlob.IsNil)
                yield return MetadataTokens.GetToken(handle.ToDefinitionHandle());
        }
    }

    public SourceLocation? GetSourceLocation(int methodToken, int ilOffset) {
        var reader = PdbMetadataReader;
        if (reader == null)
            return null;

        var debugInformation = reader.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(methodToken));
        if (debugInformation.SequencePointsBlob.IsNil)
            return null;

        // Ideally an exact match. When stepping at the end of a method there may be none,
        // the closest prior sequence point is used then
        SequencePoint? exact = null;
        SequencePoint? closest = null;
        foreach (var point in debugInformation.GetSequencePoints()) {
            if (point.IsHidden)
                continue;
            if (point.Offset == ilOffset) {
                exact = point;
                break;
            }
            if (point.Offset < ilOffset && (closest == null || point.Offset > closest.Value.Offset))
                closest = point;
        }

        var match = exact ?? closest;
        if (match == null)
            return null;

        var document = match.Value.Document.IsNil ? debugInformation.Document : match.Value.Document;
        return CreateLocation(reader, document, match.Value);
    }
    public ResolvedBreakpoint? ResolveBreakpoint(string filePath, int line, int? column, bool requireExactSource, out bool sourceMismatch) {
        sourceMismatch = false;
        var reader = PdbMetadataReader;
        if (reader == null)
            return null;

        var documentHandle = FindDocument(reader, filePath, requireExactSource, out var exactMatch, out sourceMismatch);
        if (documentHandle.IsNil)
            return null;

        var match = SequencePointResolver.Resolve(reader, documentHandle, line, column);
        if (match == null)
            return null;
        return new ResolvedBreakpoint(match.MethodToken, match.Point.Offset, CreateLocation(reader, documentHandle, match.Point), exactMatch);
    }
    public ResolvedBreakpoint? ResolveMethodEntry(int methodToken) {
        var reader = PdbMetadataReader;
        if (reader == null)
            return null;

        var debugInformation = reader.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(methodToken));
        if (debugInformation.SequencePointsBlob.IsNil)
            return null;
        // The first sequence point with source; a method that has none (only hidden ones) offers no entry to stop at
        foreach (var point in debugInformation.GetSequencePoints()) {
            if (point.IsHidden)
                continue;
            var document = point.Document.IsNil ? debugInformation.Document : point.Document;
            if (document.IsNil)
                return null;
            return new ResolvedBreakpoint(methodToken, point.Offset, CreateLocation(reader, document, point), isExactMatch: true);
        }
        return null;
    }

    // The names of the locals in scope at 'ilOffset' by slot index; a slot without a name (a compiler temporary, a
    // hidden local) is absent
    public Dictionary<int, string> GetLocalVariableNames(int methodToken, int ilOffset) {
        var names = new Dictionary<int, string>();
        var reader = PdbMetadataReader;
        if (reader == null)
            return names;

        foreach (var scopeHandle in reader.GetLocalScopes(MetadataTokens.MethodDefinitionHandle(methodToken))) {
            var scope = reader.GetLocalScope(scopeHandle);
            if (ilOffset < scope.StartOffset || ilOffset >= scope.EndOffset)
                continue;

            foreach (var variableHandle in scope.GetLocalVariables()) {
                var variable = reader.GetLocalVariable(variableHandle);
                if (variable.Attributes == LocalVariableAttributes.DebuggerHidden || variable.Name.IsNil)
                    continue;
                names.TryAdd(variable.Index, reader.GetString(variable.Name));
            }
        }
        return names;
    }
    // The IL range of the statement containing 'ilOffset': its sequence point up to the next one.
    // 'endOffset' equals 'startOffset' when the statement is the last one of the method
    public bool TryGetStepRange(int methodToken, int ilOffset, out int startOffset, out int endOffset) {
        startOffset = ilOffset;
        endOffset = ilOffset;

        var reader = PdbMetadataReader;
        if (reader == null)
            return false;

        var debugInformation = reader.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(methodToken));
        if (debugInformation.SequencePointsBlob.IsNil)
            return false;

        var points = debugInformation.GetSequencePoints()
            .Where(it => it.StartLine != 0 && !it.IsHidden)
            .OrderBy(it => it.Offset)
            .ToList();
        if (points.Count == 0)
            return false;

        // The last point at or before the offset may not exist (e.g. offset 0 without a sequence point)
        var startIndex = points.FindLastIndex(it => it.Offset <= ilOffset);
        var endIndex = points.FindIndex(it => it.Offset > ilOffset);
        if (startIndex >= 0)
            startOffset = points[startIndex].Offset;
        endOffset = endIndex >= 0 ? points[endIndex].Offset : startOffset;
        return true;
    }
    // Whether 'ilOffset' lies in a hidden region: code the compiler generated between statements, marked
    // by a sequence point that names no source line - the finally a 'using' compiles to, the plumbing
    // between two nested ones, or the yield/resume machinery of an 'await'
    public bool IsInHiddenRegion(int methodToken, int ilOffset) {
        var reader = PdbMetadataReader;
        if (reader == null)
            return false;

        var debugInformation = reader.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(methodToken));
        if (debugInformation.SequencePointsBlob.IsNil)
            return false;

        // The region the offset falls in is the one of the closest sequence point at or before it
        var closestOffset = -1;
        var closestIsHidden = false;
        foreach (var point in debugInformation.GetSequencePoints()) {
            if (point.Offset > ilOffset || point.Offset < closestOffset)
                continue;
            closestOffset = point.Offset;
            closestIsHidden = point.IsHidden;
        }
        return closestIsHidden;
    }
    // Whether 'ilOffset' lies in a finally (or fault) handler of the method
    public bool IsInFinallyHandler(int methodToken, int ilOffset) {
        // The IL of a dynamic module is not part of any image the exception regions could be read from
        if (peReader == null)
            return false;
        try {
            var method = PeMetadataReader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(methodToken));
            if (method.RelativeVirtualAddress == 0)
                return false;
            foreach (var region in peReader.GetMethodBody(method.RelativeVirtualAddress).ExceptionRegions) {
                if (region.Kind != ExceptionRegionKind.Finally && region.Kind != ExceptionRegionKind.Fault)
                    continue;
                if (ilOffset >= region.HandlerOffset && ilOffset < region.HandlerOffset + region.HandlerLength)
                    return true;
            }
            return false;
        }
        catch {
            return false;
        }
    }
    // The offset of the first sequence point with source at or after 'ilOffset'
    public int? GetNextSequencePointOffset(int methodToken, int ilOffset) {
        var reader = PdbMetadataReader;
        if (reader == null)
            return null;

        var debugInformation = reader.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(methodToken));
        foreach (var point in debugInformation.GetSequencePoints()) {
            if (point.StartLine == 0 || point.IsHidden)
                continue;
            if (point.Offset >= ilOffset)
                return point.Offset;
        }
        return null;
    }
    public AsyncMethodInfo? GetAsyncMethodInfo(int methodToken) {
        var reader = PdbMetadataReader;
        if (reader == null)
            return null;

        var result = new AsyncMethodInfo();
        foreach (var handle in reader.GetCustomDebugInformation(MetadataTokens.EntityHandle(methodToken))) {
            var debugInformation = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(debugInformation.Kind) != asyncMethodSteppingInformationGuid)
                continue;

            var blobReader = reader.GetBlobReader(debugInformation.Value);
            blobReader.ReadUInt32(); // catch handler offset
            while (blobReader.Offset < blobReader.Length) {
                var yieldOffset = blobReader.ReadUInt32();
                var resumeOffset = blobReader.ReadUInt32();
                blobReader.ReadCompressedInteger(); // MoveNext method token
                result.Awaits.Add(new AwaitInfo(yieldOffset, resumeOffset));
            }
        }
        if (result.Awaits.Count == 0)
            return null;

        var methodDebugInformation = reader.GetMethodDebugInformation(MetadataTokens.MethodDefinitionHandle(methodToken));
        if (methodDebugInformation.SequencePointsBlob.IsNil)
            return null;

        var hasUserCode = false;
        foreach (var point in methodDebugInformation.GetSequencePoints()) {
            if (point.StartLine == 0 || point.IsHidden || point.Offset < 0)
                continue;
            result.LastUserCodeOffset = point.Offset;
            hasUserCode = true;
        }
        return hasUserCode ? result : null;
    }
    public string? GetSourceLink(string documentPath) {
        if (!sourceLinkMapLoaded) {
            sourceLinkMapLoaded = true;
            sourceLinkMap = ReadSourceLinkMap();
        }
        return sourceLinkMap?.GetUrl(documentPath);
    }

    public void Dispose() {
        pdbProvider?.Dispose();
        peReader?.Dispose();
    }

    private static ModuleMetadataReader? Load(Stream stream, string? assemblyPath) {
        var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
        try {
            var result = new ModuleMetadataReader(peReader);
            result.LoadSymbols(peReader, assemblyPath);
            return result;
        }
        catch {
            peReader.Dispose();
            return null;
        }
    }
    // The file name and signature of the PDB the module was linked against, from the CodeView debug directory
    public bool TryGetPdbSignature(out string symbolFileName, out Guid pdbGuid) {
        symbolFileName = string.Empty;
        pdbGuid = Guid.Empty;
        if (peReader == null || codeViewEntry.DataSize == 0)
            return false;
        try {
            var codeViewData = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);
            symbolFileName = Path.GetFileName(codeViewData.Path);
            pdbGuid = codeViewData.Guid;
            return symbolFileName.Length > 0;
        }
        catch {
            return false;
        }
    }
    // Loads the symbols from a PDB the host located elsewhere (a search path or a symbol server)
    public bool TryLoadSymbols(string pdbPath) {
        if (HasSymbols || peReader == null || codeViewEntry.DataSize == 0)
            return false;
        return TryLoadPdbFile(peReader, pdbPath);
    }

    // The symbols of a module with an image: those of a dynamic module do not exist
    private void LoadSymbols(PEReader peReader, string? assemblyPath) {
        var embeddedPdbEntry = default(DebugDirectoryEntry);
        foreach (var entry in peReader.ReadDebugDirectory()) {
            if (entry.Type == DebugDirectoryEntryType.CodeView && entry.MinorVersion == PortableCodeViewVersionMagic)
                codeViewEntry = entry;
            else if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                embeddedPdbEntry = entry;
        }

        if (codeViewEntry.DataSize != 0 && TryLoadReferencedPdbFile(peReader, assemblyPath))
            return;
        if (embeddedPdbEntry.DataSize != 0)
            TryLoadEmbeddedPdb(peReader, embeddedPdbEntry);
    }
    // The PDB is expected next to the assembly, wherever it was built
    private bool TryLoadReferencedPdbFile(PEReader peReader, string? assemblyPath) {
        try {
            var pdbPath = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry).Path;
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (assemblyDirectory != null)
                pdbPath = Path.Combine(assemblyDirectory, Path.GetFileName(pdbPath));
            return TryLoadPdbFile(peReader, pdbPath);
        }
        catch {
            return false;
        }
    }
    private bool TryLoadPdbFile(PEReader peReader, string pdbPath) {
        MetadataReaderProvider? provider = null;
        try {
            if (!File.Exists(pdbPath))
                return false;

            var codeViewData = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);
            provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
            var reader = provider.GetMetadataReader();
            var pdbId = new BlobContentId(reader.DebugMetadataHeader!.Id);
            var expectedId = new BlobContentId(codeViewData.Guid, codeViewEntry.Stamp);
            if (codeViewData.Age != 1 || pdbId != expectedId) {
                provider.Dispose();
                return false;
            }

            pdbProvider = provider;
            PdbMetadataReader = reader;
            SymbolFilePath = pdbPath;
            return true;
        }
        catch {
            provider?.Dispose();
            return false;
        }
    }
    private bool TryLoadEmbeddedPdb(PEReader peReader, DebugDirectoryEntry embeddedPdbEntry) {
        try {
            var provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdbEntry);
            pdbProvider = provider;
            PdbMetadataReader = provider.GetMetadataReader();
            return true;
        }
        catch {
            return false;
        }
    }

    private SourceLocation CreateLocation(MetadataReader reader, DocumentHandle documentHandle, SequencePoint point) {
        var document = reader.GetDocument(documentHandle);
        var documentPath = reader.GetString(document.Name);
        var location = new SourceLocation(documentPath, point.StartLine, point.StartColumn, point.EndLine, point.EndColumn);
        location.Checksum = GetChecksum(reader, document);
        location.SourceLink = GetSourceLink(documentPath);
        return location;
    }
    private static SourceChecksum? GetChecksum(MetadataReader reader, Document document) {
        var algorithmGuid = reader.GetGuid(document.HashAlgorithm);
        string algorithm;
        if (algorithmGuid == sha256AlgorithmGuid)
            algorithm = "SHA256";
        else if (algorithmGuid == sha1AlgorithmGuid)
            algorithm = "SHA1";
        else
            return null;

        var hash = reader.GetBlobBytes(document.Hash);
        return hash.Length == 0 ? null : new SourceChecksum(algorithm, Convert.ToHexStringLower(hash));
    }
    private SourceLinkMap? ReadSourceLinkMap() {
        var reader = PdbMetadataReader;
        if (reader == null)
            return null;

        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition)) {
            var debugInformation = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(debugInformation.Kind) != sourceLinkGuid)
                continue;
            return SourceLinkMap.TryParse(Encoding.UTF8.GetString(reader.GetBlobBytes(debugInformation.Value)));
        }
        return null;
    }

    // An exact path match wins, a file name match handles PDBs built from a different location. A file name
    // match whose content hash equals the PDB's counts as exact; an unverified one is allowed only without
    // 'requireExactSource', because any module with an equally named source captures the breakpoint otherwise.
    // 'sourceMismatch' reports that only such a rejected name match was found
    private static DocumentHandle FindDocument(MetadataReader reader, string filePath, bool requireExactSource, out bool exactMatch, out bool sourceMismatch) {
        exactMatch = false;
        sourceMismatch = false;
        var normalizedPath = NormalizePath(filePath);
        var fileName = Path.GetFileName(normalizedPath);
        var fileNameMatch = default(DocumentHandle);
        var fileNameMatchVerified = false;
        var mismatchFound = false;
        foreach (var handle in reader.Documents) {
            var documentPath = NormalizePath(reader.GetString(reader.GetDocument(handle).Name));
            if (string.Equals(documentPath, normalizedPath, StringComparison.OrdinalIgnoreCase)) {
                exactMatch = true;
                return handle;
            }
            if (fileNameMatchVerified || !string.Equals(Path.GetFileName(documentPath), fileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ChecksumMatchesFile(reader, handle, filePath)) {
                fileNameMatch = handle;
                fileNameMatchVerified = true;
            }
            else if (requireExactSource)
                mismatchFound = true;
            else if (fileNameMatch.IsNil)
                fileNameMatch = handle;
        }
        exactMatch = fileNameMatchVerified;
        sourceMismatch = fileNameMatch.IsNil && mismatchFound;
        return fileNameMatch;
    }
    private static bool ChecksumMatchesFile(MetadataReader reader, DocumentHandle handle, string filePath) {
        try {
            if (!File.Exists(filePath))
                return false;
            var document = reader.GetDocument(handle);
            var documentHash = reader.GetBlobBytes(document.Hash);
            if (documentHash.Length == 0)
                return false;

            var algorithmGuid = reader.GetGuid(document.HashAlgorithm);
            using var stream = File.OpenRead(filePath);
            byte[] fileHash;
            if (algorithmGuid == sha256AlgorithmGuid)
                fileHash = SHA256.HashData(stream);
#pragma warning disable CA5350 // The checksum algorithm is chosen by the compiler that produced the PDB
            else if (algorithmGuid == sha1AlgorithmGuid)
                fileHash = SHA1.HashData(stream);
#pragma warning restore CA5350
            else
                return false;
            return fileHash.AsSpan().SequenceEqual(documentHash);
        }
        catch {
            return false;
        }
    }
    private static string NormalizePath(string path) {
        return path.Replace('\\', '/');
    }
}

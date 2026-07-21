using System;
using System.IO;
using System.Text;
using CivicSurvival.Core.Utils;

namespace CivicSurvival.Core.Diagnostics
{
    /// <summary>
    /// Minimal read-only minidump parser: recovers the faulting module + offset + exception
    /// code from a crashpad <c>.dmp</c> on next launch, so a native crash can be attributed to
    /// our code vs vanilla WITHOUT shipping the dump itself.
    ///
    /// We read only three streams — <c>ExceptionStream</c> (the faulting instruction address +
    /// code) and <c>ModuleListStream</c> (module address ranges + names) — and locate the module
    /// whose image range contains the faulting address. That single fact ("CivicSurvival_win_x86_64"
    /// vs "UnityPlayer"/"lib_burst_generated"/"Game") answers the our-code-or-vanilla question.
    ///
    /// No heap, no thread stacks, no attachments (e.g. the embedded crash screenshot) are read,
    /// so nothing beyond a module file name leaves the machine. The module name is run through
    /// <see cref="PiiRedactor"/> by the caller as belt-and-suspenders; this reader already returns
    /// the file name only (path stripped), so the OS user name in the module path never escapes.
    /// Every read is bounds-guarded against the file length — a truncated or corrupt dump returns
    /// <see cref="FaultInfo.None"/> rather than throwing.
    /// </summary>
    public static class MinidumpStackReader
    {
        private const uint MinidumpSignature = 0x504D444D; // 'MDMP'
        private const uint ExceptionStreamType = 6;
        private const uint ModuleListStreamType = 4;
        private const int ModuleRecordBytes = 108;
        private const int ModuleListHeaderBytes = 4;  // NumberOfModules
        private const int DirectoryEntryBytes = 12;   // MINIDUMP_DIRECTORY: StreamType(4) + Location(8)
        private const int ExceptionReadBytes = 40;    // bytes consumed from the exception stream base (id..address)
        private const int MaxModules = 100_000;       // sanity cap against a corrupt count
        private const int MaxStreams = 1024;
        private const uint MaxModuleNameBytes = 2048;

        private static readonly char[] PathSeparators = { '\\', '/' };

        private static readonly LogContext Log = new("MinidumpStackReader");

        public readonly struct FaultInfo
        {
            public readonly bool Found;
            public readonly string Module;       // faulting module file name (no path), e.g. CivicSurvival_win_x86_64.dll
            public readonly ulong Offset;        // faulting instruction offset within that module
            public readonly uint ExceptionCode;  // e.g. 0xC0000005 (access violation)

            public FaultInfo(string module, ulong offset, uint exceptionCode)
            {
                Found = true;
                Module = module ?? string.Empty;
                Offset = offset;
                ExceptionCode = exceptionCode;
            }

            public static FaultInfo None => default;
        }

        /// <summary>
        /// Parse the faulting module/offset/code from a minidump. Returns <see cref="FaultInfo.None"/>
        /// when the file is missing, not a minidump, has no exception stream (e.g. a non-crash dump),
        /// or is too corrupt to read. Never throws.
        /// </summary>
        public static FaultInfo TryReadFault(string? dumpPath)
        {
            if (string.IsNullOrEmpty(dumpPath))
                return FaultInfo.None;

            try
            {
                using var fs = new FileStream(dumpPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long length = fs.Length;
                using var br = new BinaryReader(fs, Encoding.Unicode, leaveOpen: true);

                if (br.ReadUInt32() != MinidumpSignature)
                    return FaultInfo.None;

                _ = br.ReadUInt32();                       // Version
                uint streamCount = br.ReadUInt32();
                uint dirRva = br.ReadUInt32();

                if (streamCount == 0 || streamCount > MaxStreams || !InRange(dirRva, (long)DirectoryEntryBytes * streamCount, length))
                    return FaultInfo.None;

                uint exceptionRva = 0;
                uint moduleRva = 0;
                for (uint i = 0; i < streamCount; i++)
                {
                    fs.Seek(dirRva + i * (long)DirectoryEntryBytes, SeekOrigin.Begin);
                    uint type = br.ReadUInt32();
                    _ = br.ReadUInt32();                   // DataSize
                    uint rva = br.ReadUInt32();
                    if (type == ExceptionStreamType) exceptionRva = rva;
                    else if (type == ModuleListStreamType) moduleRva = rva;
                }

                // Without an exception stream there is no faulting address to attribute.
                if (exceptionRva == 0 || moduleRva == 0)
                    return FaultInfo.None;

                // ExceptionStream: ThreadId(4) __align(4) | MINIDUMP_EXCEPTION { Code(4) Flags(4)
                // Record(8) Address(8) ... }. Read Code and Address sequentially from the stream base.
                if (!InRange(exceptionRva, ExceptionReadBytes, length)) return FaultInfo.None;
                fs.Seek(exceptionRva, SeekOrigin.Begin);
                _ = br.ReadUInt32();                       // ThreadId
                _ = br.ReadUInt32();                       // __alignment
                uint exceptionCode = br.ReadUInt32();
                _ = br.ReadUInt32();                       // ExceptionFlags
                _ = br.ReadUInt64();                       // ExceptionRecord
                ulong faultAddress = br.ReadUInt64();

                // ModuleList: NumberOfModules(4) then MINIDUMP_MODULE[108]. Collect ranges + name RVAs
                // sequentially, then resolve the one containing the faulting address.
                if (!InRange(moduleRva, ModuleListHeaderBytes, length)) return FaultInfo.None;
                fs.Seek(moduleRva, SeekOrigin.Begin);
                uint moduleCount = br.ReadUInt32();
                if (moduleCount == 0 || moduleCount > MaxModules)
                    return FaultInfo.None;

                long modulesStart = moduleRva + ModuleListHeaderBytes;
                if (!InRange(moduleRva, ModuleListHeaderBytes + (long)moduleCount * ModuleRecordBytes, length))
                    return FaultInfo.None;

                uint hitNameRva = 0;
                ulong hitBase = 0;
                for (uint i = 0; i < moduleCount; i++)
                {
                    fs.Seek(modulesStart + (long)i * ModuleRecordBytes, SeekOrigin.Begin);
                    ulong baseOfImage = br.ReadUInt64();
                    uint sizeOfImage = br.ReadUInt32();
                    _ = br.ReadUInt32();                   // CheckSum
                    _ = br.ReadUInt32();                   // TimeDateStamp
                    uint nameRva = br.ReadUInt32();

                    if (faultAddress >= baseOfImage && faultAddress < baseOfImage + sizeOfImage)
                    {
                        hitNameRva = nameRva;
                        hitBase = baseOfImage;
                        break;
                    }
                }

                if (hitNameRva == 0)
                    return FaultInfo.None;                 // faulting address outside every loaded module

                string fileName = ReadModuleFileName(br, fs, hitNameRva, length);
                if (string.IsNullOrEmpty(fileName))
                    return FaultInfo.None;

                return new FaultInfo(fileName, faultAddress - hitBase, exceptionCode);
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Minidump parse failed: {ex.Message}");
                return FaultInfo.None;
            }
        }

        // MINIDUMP_STRING at rva: Length(4, byte count of UTF-16, no terminator) + WCHAR[]. The name is
        // a full path; return the file name only so the OS user name in the path is never read out.
        private static string ReadModuleFileName(BinaryReader br, FileStream fs, uint rva, long length)
        {
            if (!InRange(rva, 4, length)) return string.Empty;
            fs.Seek(rva, SeekOrigin.Begin);
            uint nameBytes = br.ReadUInt32();
            if (nameBytes == 0 || nameBytes > MaxModuleNameBytes || !InRange(rva + 4, nameBytes, length))
                return string.Empty;

            var raw = br.ReadBytes((int)nameBytes);
            string full = Encoding.Unicode.GetString(raw);
            int slash = full.LastIndexOfAny(PathSeparators);
            return slash >= 0 && slash + 1 < full.Length ? full.Substring(slash + 1) : full;
        }

        private static bool InRange(uint rva, long size, long fileLength)
            => rva > 0 && size >= 0 && (long)rva + size <= fileLength;
    }
}

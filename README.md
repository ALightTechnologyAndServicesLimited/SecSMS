# SecSMS

This repository contains code for 2 applications.
Android and Windows.

Documentation: [SecSMS Protocol](PROTOCOL.md) — Bluetooth framing, ML-KEM-768 + X25519 + AES-256-GCM cryptography, OTP/TOTP message flows, payload formats, and security properties.


## Problem:
There are cyber criminals using invisible drones capable of invisible cameras, invisible speakers and mind reading capabilities equipment. They are doing OTP thefts, identity/credentials thefts, hacking accounts, enacting like virtual family, virtual friends, and doing identity distortion. This small tool reduces their cyber mafia activities of OTP theft!

March 1 2026: Added URL masking functionality to hide sensitive URLs in SMS messages. Allows opening URLs in browser without exposing the full URL. Some identity verification services require opening URLs in browser and have codes for a particular session.

April 12 2026: PQ algorithms.


[You update your Passwords but NOT TOTP!
](https://blog.alightservices.com/2026/10/you-update-your-passwords-but-not-totp.html)

October 14 2026: TOTP authenticator in the Maui app (RFC 6238, SHA1/SHA256/SHA512, 6-8 digits). Add accounts by scanning the enrollment QR code with the camera or by manual entry. Codes are never displayed — each account shows only •••••• with a countdown; use Copy or Send to Windows (PQ-encrypted Bluetooth, same channel as SMS OTPs). Accounts can be renamed or deleted. Secrets are stored in SecureStorage (Keystore/Keychain).

October 14 2026: Biometric app lock in the Maui app. Lock with face/fingerprint (Strong/Class 3 only). The app locks when sent to background and shows a lock screen on return. Turn it on with the switch under Links → Security, or tap Lock in the toolbar (enables and locks in one tap). Requires Android 8.0+ and enrolled biometrics; needs CAMERA and USE_BIOMETRIC permissions.

November 14 2026: TOTP enrollment without showing the secret on screen (clipboard import, Windows → Android). For websites that copy a masked TOTP instead of displaying the QR/secret: copy the otpauth://totp/... URI or even a bare Base32 secret, then push it to the phone over the PQ-encrypted Bluetooth channel. Usage: Start Bluetooth on Windows → on Android open TOTP and tap "From Desktop" and pick the Windows device (the phone sends only its public ML-KEM-768 + X25519 key bundle; private keys never leave the phone) → on Windows click "Send TOTP from clipboard" → confirm the masked prompt (shows only issuer/account, never the secret). Windows validates the clipboard, encrypts with the phone's public bundle, sends, and clears the clipboard. Android decrypts, and if issuer/account are missing (bare secret) prompts for them before saving. The secret is never displayed or logged on either side, so screen spies and BT sniffers cannot steal it. Notes: one handshake per import — to import another account, tap "From Desktop" again; bare secrets use SHA1/6 digits/30s defaults; device authentication relies on Bluetooth pairing, so pick the correct paired device; Windows clipboard history (Win+V) may retain the copied secret independently of the app clearing the clipboard.

November 14 2026: TOTP list fixes. Rows now show the issuer and the account name on separate lines; entries with no issuer/account get a unique "Unnamed account (xxxxxx)" label so they can be told apart and renamed or deleted. Re-scanning the same QR or re-importing the same account now updates the existing entry instead of creating duplicates (dedupe by normalized secret). Manual entry requires at least an issuer or an account name.

Full protocol documentation (Bluetooth framing, ML-KEM-768 + X25519 + AES-256-GCM construction, message flows, payload formats, security properties): [PROTOCOL.md](PROTOCOL.md)


The next problem caused by them session hijacking! Something needs to be done!
[openvpn27-ubuntu-script](https://github.com/ALightTechnologyAndServicesLimited/openvpn27-ubuntu-script)

[ALightVPN](https://vpn.alightservices.com)

I won't join the cyber mafia nor give any ransom extortion!


### Personal Links:
[Facebook - Kanti Arumilli](https://www.facebook.com/kanti.arumilli)

[LinkedIn - Kanti Kalyan Arumilli](https://www.linkedin.com/in/kanti-kalyan-arumilli/)

[Twitter](https://twitter.com/KantiKalyanA/)

[Thread](https://www.threads.net/@kantiarumilli)

[Youtube](https://www.youtube.com/@kantikalyanarumilli)

+91-789-362-6688, +1-480-347-6849, +44-07718-273-964

### Startup Links:
[Facebook](https://www.facebook.com/ALightTechnologyAndServicesLimited/)

[LinkedIn](https://www.linkedin.com/company/alight-technology-and-services-limited/)

[Youtube](https://www.youtube.com/@alighttechnologyandservicesltd)

[Website](https://www.alightservices.com/)

[WebVeta](https://webveta.alightservices.com/)
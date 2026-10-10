# Independent SPEC58 — SPEC_GREEN

Disposition: **SPEC_GREEN** for isolated candidate `ca354d2194a454b0356057c2db34e94cea8ffbbe`. Zero blocking specification findings. This approves the scoped alias-command correction and its provenance/validation claims for further review; it is not release approval or physical Huawei/Windows/UI acceptance. Separate QUALITY59 remains next.

Task `spec`; attempt `gpc-huawei-spec58-20261010`; reservation `983e4bc2-9ca0-462c-b2d4-b42adb089c16`. Parent supplied verified running admission at coordination `c58f5d31de184b38cae56a12ad43861dcaa0454b`, `exe-gpc-recovery/max1010/final-{plan,state}.json`. This reviewer used exact Git objects, not dirty working-tree files, and wrote only this report pair. No source/Git/backend/pool/lease mutation or delegation. CDC 2.12.1 and original CI6 UNKNOWN remain unchanged.

## Exact provenance independently checked

- Candidate tree: `ad95b455107256e7288f53fa04bb249f397e7ad7`; sole parent: `68da62237ecfa5377fcc31c5641b3baaebea39a9`.
- Introduced history is exactly one commit and two touched paths: HuaweiFirmwareSecurityAdapter.cs and FirmwareSecurityCollectorSelfTest.cs. Prior UI files, collector semantics and other base files have no candidate delta.
- Raw commit bytes exactly equal `git cat-file commit`; recomputed Git object identity equals the candidate. Bundle advertises that candidate; bundle SHA-256 is `f8fe0a5c1f058ecdcc0f2142210902d9961abac8b3e8fac78d84188899e88b18`.
- Prepared RED packet content sizes, SHA-256 and Git blob identities were checked. Original-source hashes match exact68da. Final source is exactly that prepared RED source with the sole `/v` argument removed; its test file is otherwise identical to prepared RED. Embedded authenticated-handoff source and local handoff copies match exact candidate objects.
- Source patch exactly matches `git diff --abbrev=8` for base→candidate, SHA-256 `f1a7a172b4fabd11ee468a332facfd6654196ed6954a2144899769628da7d8a2`. Initial default-diff comparison differed only in 7-versus-8-character index abbreviations; the source/hunk identity was then verified exactly.
- Final adapter SHA-256: `5486e853465a3a91811752cd072becef685c96bea5374483646da9fb6b648ef1`; final selftest SHA-256: `3a5cfbd42594bdf922e273d22fc419eb985769449cb1e76124b7b2d0f0d215f8`.
- Delivered EXE independently hashes to `484b95a877a2e2543ce2937598ab58ea9d51e0fa29d2d6ff5ca5dd732adf41cf`, exactly 78,764,002 bytes. PE signature, AMD64 machine, PE32+ and GUI subsystem were checked. Root's actual version/informational-commit and 453 bundle-entry checks are retained as supplied authenticated evidence, not claimed as checks rerun by this reviewer.

## Requirements and findings

**F1 — INFO, command/parser contract corrected.** `CreateFirmwareEnumerationStartInfo(path)` returns exactly `/enum`, `FIRMWARE`; `/v` is absent. The production reader uses this helper. The parser still locates `{fwbootmgr}`; Microsoft states that `/v` prints full identifiers instead of well-known names, so removing it aligns the query with the existing alias parser. The new production selftest checks executable path, exact argument sequence and shell-free redirected collection settings. Fresh official source: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/bcdedit--enum . Administrative access requirements remain; removal of `/v` does not grant access.

**F2 — INFO, collection/error/Unknown boundaries preserved.** The helper retains UseShellExecute=false, stdout/stderr redirection and CreateNoWindow. System executable path resolution, file absence handling, three-second process wait, existing kill/wait behavior, output/error drain and nonzero-exit→null behavior are unchanged. Exceptions still flow to adapter Unknown fallback. Parser and collector are unchanged: no external entry remains null, incomplete/untrusted BCD evidence remains Unknown, and absence does not produce a boot-restriction Pass. Positive external-path/order cases continue to supply risk evidence rather than proof that every firmware restriction is disabled. No new overall output-memory/drain-deadline guarantee is claimed for the unchanged collection code.

**F3 — INFO, no firmware/OEM scope expansion.** The exact diff introduces no OEM invocation, guessed opcode, DLL load, driver/remediation install, password material, BIOS/BCD writes or whole-application elevation. Password telemetry remains the existing SMBIOS Type24 status path; the candidate does not establish an independent OEM ABI or Huawei password/USB/PXE GET contract.

**F4 — INFO, supplied validation is bounded and honest.** Native57's authenticated portable-harness evidence has exactly one RED failure in the new argument-contract test, summary 11/12 and exit1, followed by 12 PASS cases, summary 12/12 and exit0 after the one-line fix. The harness invokes production FirmwareSecurityCollectorSelfTest with fake sources and production dependencies; it is not a real BCD/WMI/OEM query or full-application Windows suite. Supplied SDK8.0.425 publish result is exit0, zero warnings/errors, using cached assets and retained brand assets. Fresh brand generation is NOT_RUN. This reviewer checked log content/binding and source correspondence but did not start/rerun a backend, compiler or tests.

Full application tests, Windows11/UAC, interactive UI/DPI/RDP and actual Huawei hardware remain **NOT_RUN**. Prior SPEC47/QUALITY49 are retained for the unchanged base, not promoted into new runtime proof. The known previous Windows fixture failures remain disclosed in verification. OEM package fetch is recorded as proxy CONNECT403, zero bytes and zero retries; ABI remains unknown, and this is not evidence of no OEM interface.

## Evidence bindings and timing

- Authenticated handoff SHA-256: `f696b3b4653c6d6af3abbbdf28175700d6585d44d83e45ce2d8a8e31b508d1be`.
- Manifest SHA-256: `e696d1ed63382d396045cd85b6c719bdf78191f2b8bb2f63843c0c219ca71836`.
- Prepared RED packet SHA-256: `bab71adcba3359aa8f395f74bb934f08f41c6763cdfc2a177f51b449fe530f21`.
- Delivered verification JSON SHA-256: `c1e6bc3e6e78486271f9352bb60d28fab4c403b977cb30cd1b6d38c078cd502f`.

First genuinely observed review clock: `2026-10-10T10:01:47Z`. Report-preparation finish: `2026-10-10T10:05:14Z`. Provider/physical worker launch time unknown. These are this review's local observation times, not Native57 or provider-duration claims. Root integration, independent QUALITY59 and required Windows/interactive/hardware gates remain outstanding; no product source was published by this review.

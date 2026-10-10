# OEM56 — новые проверенные пути исследования Huawei BIOS

Результат: **RESEARCH_COMPLETE_WITH_CONCRETE_NEXT_STEP**. Найден официальный источник пакета PC Manager и описан путь статического анализа DLL/ACPI без выполнения OEM-команд. Подтверждённого read-only GET для BIOS password, USB/PXE restrictions или нужного селектора MCLF-XX пока нет. Это завершение ограниченного исследования, не доказательство отсутствия интерфейса и не разрешение продуктовой интеграции.

Task `oem`, attempt `gpc-huawei-OEM56-20261010`, token `e6ee8f9c-6358-4da5-8206-42df594f5291`; parent `Work-root-gpc-20261010-123856`; parent-supplied running claim coordination `b81e276bd1e8393613254a0fb63cb1f688195d8c`, `exe-gpc-recovery/max1010/oem-state.json`. Existing guard retained; CDC 2.12.1 unchanged. Only this review's MD/JSON are written. No source, Git, backend, OEM calls, drivers, guessed opcodes, DLL loading or delegation.

## Новое подтверждение 1: официальный пакет для статического анализа

Huawei's current global PC Manager page advertises **14.0.7.260** and exposes Download Now [H1]. The observed stable official download route is:

https://consumer-tkb.huawei.com/weknow/servlet/download/public?contextNo=W00030690&view=true

It redirects to an opaque EXE URL on `consumer-tkbdownload.huawei.com` [H2]. The web tool reported that the EXE is not accessible as page content after the redirect. This is a retrieval-tool limitation, not proof of a Huawei outage. No binary was downloaded, installed, executed, hashed, unpacked or inspected. The page version is not a verified version of the executable or any embedded DLL. Huawei's own instructions identify this official download route and caution that available features vary by model [H3]. The package's contents, packaging format, architecture and presence of WmiUtil.dll/WmiUtils.dll remain unverified.

**Concrete next eligible offline inspection:** obtain that exact official payload through an authorized binary-transfer route; record SHA-256, source URL, size and signature status; inspect/extract as archive data only, with size limits and no installer execution. Determine packaging format from bytes rather than assume NSIS/MSI. If a bootstrapper contains no DLLs, record this and stop; do not run it to obtain dependencies. Compare package and installed-device DLL versions/hashes when local evidence becomes available.

For each actual DLL, statically inspect PE exports/imports/RVAs/forwarders and decorated names. Microsoft documents `dumpbin /exports <file.dll>` as an export-listing operation [M1]. The PE structure records names/ordinals and locations; names may be omitted, and entries may forward to other modules [M2]. C++ name decoration is compiler-specific [M3]. Therefore the old ordinals 16/20/21 must not be treated as stable identities or usable ABI. Do not call the functions.

An implementation-analysis handoff should trace GetBiosInfo/GetDeviceBiosSwitchStatus and their call sites to the WMI input builder; establish selector enum values, object/this initialization, buffer layouts, return/error semantics and any forwarded implementation. Record exact byte hashes and instruction offsets. A discovered constant is evidence for analysis, not permission to send an opcode. Names containing Get do not establish read-only behavior; the concrete dispatch path and firmware-side handling must be reviewed before any future invocation is considered.

## Новое подтверждение 2: ACPI discovery ≠ telemetry command

Primary kernel documentation specifies a static `_WDG` array of 20-byte records carrying GUID, method/notification ID, instance count and flags; PNP0C14 devices provide the mapping [A1]. The method flag identifies a callable method block, not a read-only guarantee. `WMxx` dispatches WMI methods with instance, method ID and argument buffer. `_WED` retrieves event data by notification ID; it is not a generic BIOS-settings reader. `WQxx` is a data-query path, while collection/event enable methods are distinct. No MCLF-specific password/USB/PXE opcode follows from this generic mapping.

Microsoft documents desktop `EnumSystemFirmwareTables('ACPI')` / `GetSystemFirmwareTable('ACPI', id)` for table enumeration/readback [M4/M5]. This is a possible **driver-free read-only acquisition design**, not an action performed here. Important coverage limit: duplicate signatures can be enumerated, but GetSystemFirmwareTable returns only the first matching table. A stack of SSDTs is therefore not proven complete merely by using this API. If DSDT/required SSDTs are unavailable, record Missing/IncompleteCoverage rather than infer an absent OEM interface.

**Concrete device-side evidence plan, still NOT_RUN:** first obtain a bounded signature inventory/status; retain only the needed ACPI bytes locally for static analysis, with privacy review before sharing any excerpt. Disassemble AML offline without evaluating methods. Locate PNP0C14, the static `_WDG` GUID record, corresponding WM/WQ method names and the body of the dispatcher. Inspect selector cases, return-package format and all side-effect paths relevant to a candidate GET. Export only sanitized mapping/evidence excerpts, not a full SMBIOS/ACPI/PII dump. An unresolved external method or missing SSDT keeps read-only behavior unproven. ACPICA's own project discussion documents external/dynamic SSDT coverage problems during disassembly [A2]. Do not use firmware override, acpiexec, EC/register access or runtime `_WDG`/`_WED`/WM method evaluation to substitute for this static proof.

A newly checked author DSDT repository is for Honor Magicbook Pro14 2025 FMB-P/BIOS1.13, not MCLF-XX [X1]; its tables and patch instructions do not establish the required Huawei model mapping and are not adopted.

## Точный оставшийся пробел

Focused primary-source checks for `DeviceBiosSwitchNumber`, GetDeviceBiosSwitchStatus outside the already known author table, GetBiosInfo/WmiUtil and MCLF-specific DSDT/WMI found no usable primary ABI/selector/command contract. The blank-opcode table is retained as prior evidence, not reported as new progress. This bounded search cannot prove that Huawei has no private SDK or that no future source can establish the contract.

Required evidence still missing: exact actual DLL bytes and disassembly; exact MCLF firmware ACPI mapping and complete dependency coverage; selectors for password/boot policy; response/error layout; demonstrated read-only dispatch; physical Windows/Huawei results. Win32_ComputerSystem remains the same SMBIOS Type24 source and is not a new independent fallback. Unknown must remain Unknown. No password retrieval or bypass is proposed.

## Sources freshly checked

- [H1] https://consumer.huawei.com/en/support/pc-manager/ — Official page advertises PC Manager 14.0.7.260 and exposes official Download Now route
- [H2] https://consumer-tkb.huawei.com/weknow/servlet/download/public?contextNo=W00030690&view=true — Observed official download redirects to consumer-tkbdownload.huawei.com .exe; binary unsupported by web retrieval tool, no bytes/hash acquired
- [H3] https://consumer.huawei.com/en/support/content/en-us00688514/ — Official download instructions; features vary by Huawei model
- [M1] https://learn.microsoft.com/en-us/cpp/build/exporting-from-a-dll?view=msvc-170 — DUMPBIN /EXPORTS lists DLL export table
- [M2] https://learn.microsoft.com/en-us/windows/win32/debug/pe-format — PE names/ordinals/RVAs and forwarders, optional export names; table alone is not a full callable contract
- [M3] https://learn.microsoft.com/en-us/cpp/build/reference/exports?view=msvc-170 — C++ decorated names are compiler-specific; NONAME exports can omit names
- [A1] https://www.kernel.org/doc/html/v6.8/wmi/acpi-interface.html — Static _WDG discovery mapping; WMxx method dispatch, WQxx queries and _WED event data are distinct
- [M4] https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-enumsystemfirmwaretables — Desktop API enumerates ACPI signatures, including duplicates
- [M5] https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemfirmwaretable — Desktop API reads ACPI table bytes, but only first table for duplicate signature
- [A2] https://github.com/acpica/acpica/issues/414 — ACPICA primary project record warns incomplete external/dynamically loaded SSDTs can make disassembly unresolved
- [X1] https://github.com/denis-bb/honor-fmb-p-dsdt — Author DSDT artifacts are Honor FMB-P/BIOS1.13, not Huawei MCLF; cannot reuse target mapping

First observed local clock: `2026-10-10T09:44:28Z`. Research/report-preparation finish: `2026-10-10T09:47:31Z`. Physical/provider/agent start time unknown. Windows/Huawei runtime, package binary acquisition, DLL export/ABI inspection and ACPI capture/disassembly are **NOT_RUN**. Root may now pursue the concrete official-package acquisition/static-inspection step while keeping existing product/backend guards intact.

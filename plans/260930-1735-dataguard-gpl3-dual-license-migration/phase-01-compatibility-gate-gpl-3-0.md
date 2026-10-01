---
phase: 1
title: "Compatibility gate GPL-3.0 (BLOCKING cho Phase 2)"
status: completed
priority: P1
effort: "3h"
dependencies: []
---

# Phase 1: Compatibility gate GPL-3.0

<!-- Updated: Red Team + Validation Session 1 - D1–D6 đã chốt (ck-predict, owner ủy quyền 2026-09-30); thêm bảng kênh phân phối (S4/A2) và Tests-first -->

## Overview
Kiểm kê giấy phép mọi thành phần bên thứ ba được **phân phối** cùng DataGuard và **liệt kê mọi kênh phân phối (conveyance) theo từng thành phần** — vì Oracle ODP.NET và SNI runtime đi vào nuget (tool nupkg), zip theo RID, ghcr image và VSIX mà không kênh nào chứa văn bản giấy phép hôm nay. Các quyết định D1–D6 **đã chốt** (mục "Quyết định" bên dưới); phase này xác nhận bằng bằng chứng lệnh (RED) rằng trạng thái hiện tại thiếu notice, và bàn giao bảng kênh cho P2. Phase **không sửa file production**.

## Requirements
- Functional: bảng compat cho từng thành phần (tái kiểm chứng); bảng kênh phân phối × thành phần (Oracle, SNI, Contracts, Analyzers, SqlClassification, LanguageServer) với lệnh kiểm nội dung artifact cho mỗi kênh; baseline `check-nuget-licences.py` xanh.
- Non-functional: mọi kết luận pháp lý ghi "không phải tư vấn pháp lý; văn bản chưa qua luật sư (V2)"; không tự soạn điều khoản mới ngoài mẫu FSF.

## Architecture (dữ liệu bằng chứng, quét 2026-09-30)
Nguồn: `src/*/packages.lock.json` → `<license>` trong `~/.nuget/packages/<id>/<ver>/<id>.nuspec`; npm từ `src/DataGuard.VSCode/package-lock.json` (bỏ `dev:true`).

| Nhóm | Số gói | Kết luận với GPL-3.0 |
|---|---|---|
| MIT (Roslyn, EF Core, ScriptDom, YamlDotNet, MySqlConnector, System.CommandLine…) | 135 | Tương thích |
| Apache-2.0 (AWSSDK.SecretsManager, OpenTelemetry*…) | 12 | Tương thích GPLv3 (không GPLv2 → lý do không dùng GPL-2.0) |
| PostgreSQL licence (Npgsql 10.0.3) | 1 | Tương thích |
| npm production: MIT 6, ISC 2 | 8 | Tương thích |
| Legacy licenseUrl "MS .NET Library" | 8 | Mã MIT trên GitHub; build-time/reference; không chặn |
| **`Oracle.ManagedDataAccess.Core` 23.26.301** | 1 | **Không tương thích nguyên trạng** (LICENSE.txt: chỉ redistribute unmodified, cấm reverse engineering, phải kèm bản giấy phép, không thu phí riêng cho driver) → xử lý bằng §7 additional permission (D2) + kèm nguyên văn FDHUT trong mọi kênh |
| **`Microsoft.Data.SqlClient.SNI.runtime` 7.1.0** | 1 | **Không tương thích nguyên trạng** (Microsoft Software License Terms, pass-through, indemnity); **không** phải "System Library" vì DataGuard tự phân phối nó → D2 |
| Microsoft.VisualStudio.* / VSSDK (VSIX) | ~74 | Compile-time; không tái phân phối (`DataGuard.VisualStudio.csproj:43-55`). VSIX chạy trong host độc quyền → §7 nêu VS/VS Code là host |
| MS-PL, MPL-2.0 (`scripts/allowed-licences.txt:18,23`) | 0 | Không dùng thực tế; giữ allow-list |

<!-- Updated: Red Team + Validation Session 1 - S4/A2/A6: bảng kênh phân phối × thành phần -->
**Kênh phân phối (conveyance) — mỗi kênh phải mang LICENSE + THIRD-PARTY-NOTICES + ADDITIONAL-PERMISSIONS từ v0.4.0:**

| Kênh | Tạo bởi | Chứa Oracle/SNI? | Chứa Contracts/Analyzers/SqlClassification? | Notice hôm nay |
|---|---|---|---|---|
| `DataGuard.Cli.<ver>.nupkg` (dotnet tool, `PackAsTool=true` `DataGuard.Cli.csproj:9`; ProjectReference Oracle/adapters :19-23) — **có** đẩy lên nuget.org dù comment `release.yml:397` nói ngược | `release.yml:114-118` (`dotnet pack DataGuard.CrossPlatform.slnf`) | Có | Có (Analyzers→Contracts, SqlClassification) | Không |
| `dataguard-<ver>-<rid>.zip` (win-x64, linux-x64, osx-arm64) | `release.yml:449`; nightly `installers.yml:159` | Có | Có | Không |
| ghcr.io image (`Dockerfile:29` copy Oracle csproj; publish :47-52; chỉ label `source`/`description` :64-65) | `release.yml:744-748` | Có | Có | Không; **không** có `org.opencontainers.image.licenses` |
| `DataGuard.VisualStudio.vsix` (nhúng `cli/dataguard.exe`) | `ci.yml:202-208` (artifact mỗi push/PR), `marketplace.yml`, `installers.yml` | Có | Có | Chỉ `LICENSE.txt` (MIT) |
| `dataguard-vscode-<ver>.vsix` | `marketplace.yml:126-130`, `installers.yml` | Không (LanguageServer + client) | LanguageServer/SqlClassification | Chỉ `LICENSE` (MIT) |
| 14 nupkg thư viện còn lại (v0.3.0: 15 nupkg kể cả `SqlClassification`, `LanguageServer`) | `release.yml:114-118` | `Oracle.Adapter` nupkg: dependency (không nhúng) | `Analyzers` nupkg nhúng `Contracts.dll` + `SqlClassification.dll` (`DataGuard.Analyzers.csproj:11` `IncludeBuildOutput=true`) → D4 | Không (10/16 project chưa có licence metadata) |

Phân tích thành phần của chính DataGuard:
- `DataGuard.Contracts` (netstandard2.0): `ContractAttributes.cs` không `[Conditional]`; Core đọc attribute bằng reflection (`src/DataGuard.Core/Sources/ManualContractSource.cs:33-83`) → `Contracts.dll` đi kèm binary closed-source của khách = linking thật → D3.
- `DataGuard.Analyzers`: `IncludeBuildOutput=true` + comment "Not a DevelopmentDependency" (`DataGuard.Analyzers.csproj:11-14`) → Analyzers.dll + Contracts.dll + SqlClassification.dll chảy vào `bin/` của khách như thư viện thường → D4.

## Related Code Files
- Đọc: `src/*/packages.lock.json`, `src/DataGuard.VSCode/package-lock.json`, `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj:43-55,105-125`, `scripts/allowed-licences.txt`, `scripts/check-nuget-licences.py`, `DataGuard.Cli.csproj`, `DataGuard.Analyzers.csproj`, `Dockerfile`, `release.yml`, `installers.yml`, `marketplace.yml`.
- Ghi: chỉ file phase này. Không tạo/xóa file khác.

<!-- Updated: Red Team + Validation Session 1 - TDD: mục Tests-first -->
## Tests-first (Red → Green)
Chạy trên Git Bash Windows (`unzip` có sẵn; `python3` khớp `ci.yml:74`). Ghi output vào mục "Kết quả RED" cuối phase.
1. Baseline gate (GREEN hôm nay, phải giữ GREEN): `python3 scripts/check-nuget-licences.py` → exit 0.
2. Graph vs bảng kênh: `dotnet list DataGuard.CrossPlatform.slnf package --include-transitive | grep -iE "Oracle.ManagedDataAccess|SqlClient.SNI"` → cả hai xuất hiện dưới Cli/Oracle.Adapter/Core (bằng chứng cho cột "Chứa Oracle/SNI").
3. Nuspec hiện tại: `grep -il "reverse" ~/.nuget/packages/oracle.manageddataaccess.core/23.26.301/LICENSE.txt` và `head -3 ~/.nuget/packages/microsoft.data.sqlclient.sni.runtime/7.1.0/LICENSE.txt` → xác nhận marker của allow-list.
4. Assert nội dung artifact (RED = **thiếu** notice hôm nay), lấy artifact v0.3.0/nightly bằng `gh release download v0.3.0 -p 'dataguard-0.3.0-win-x64.zip' -p 'DataGuard.Cli.0.3.0.nupkg' -D "$TMP"`:
   - `unzip -l "$TMP/dataguard-0.3.0-win-x64.zip" | grep -ciE "LICENSE|THIRD-PARTY|ADDITIONAL-PERMISSIONS"` → 0 (RED).
   - `unzip -l "$TMP/DataGuard.Cli.0.3.0.nupkg" | grep -ciE "LICENSE|THIRD-PARTY"` → 0 (RED); `unzip -p "$TMP/DataGuard.Cli.0.3.0.nupkg" DataGuard.Cli.nuspec | grep -c "<license"` → 1 (MIT).
   - `unzip -l "$TMP/DataGuard.Cli.0.3.0.nupkg" | grep -ciE "Oracle.ManagedDataAccess|SNI"` → >0 (Oracle/SNI có trong tool nupkg).
   - VSIX: `gh release download nightly -p 'DataGuard.VisualStudio-*.vsix' -D "$TMP"`; `unzip -l "$TMP"/DataGuard.VisualStudio-*.vsix | grep -ciE "THIRD-PARTY|ADDITIONAL"` → 0 (RED).
   - Docker (cần daemon; tag `0.3.0` theo `type=semver,pattern={{version}}` `release.yml:781`): `docker run --rm --entrypoint ls ghcr.io/thanhnt-sm/eco_support_net_oracle:0.3.0 /app | grep -ciE "LICENSE|THIRD-PARTY"` → 0 (RED); `docker buildx imagetools inspect ghcr.io/thanhnt-sm/eco_support_net_oracle:0.3.0 --format '{{json .}}' | grep -c image.licenses` → 0 (RED).
5. GREEN của P1 = bảng kênh đầy đủ và mỗi dòng có lệnh assert; các assert 4 chuyển sang GREEN ở P2/P6.

## Implementation Steps
1. Chạy các lệnh RED ở mục Tests-first và ghi kết quả (lệnh + output rút gọn) vào "Kết quả RED".
2. Tái quét phần chưa phủ: `src/DataGuard.VisualStudio/obj/cli-publish.packages.lock.json` (`csproj:112`, cần build VSIX một lần) và runtime pack theo RID — ghi "MIT, đã kiểm" hoặc liệt kê ngoại lệ.
3. Hoàn thiện bảng kênh × thành phần (bên trên) nếu phát hiện kênh mới; mỗi dòng phải có lệnh assert tương ứng để P2/P6 tái dùng.
4. Đối chiếu văn bản Oracle FDHUT (`~/.nuget/packages/oracle.manageddataaccess.core/23.26.301/LICENSE.txt`) và SNI LICENSE.txt: trích các nghĩa vụ phải thể hiện trong `docs/legal/THIRD-PARTY-NOTICES.md` (P2): unmodified redistribution, kèm bản giấy phép, không thu phí riêng cho driver, không reverse engineering; SNI: pass-through terms, indemnity.
5. Bàn giao cho P2: bảng kênh + trích nghĩa vụ + danh sách 13 nupkg mục tiêu (xem P2).

<!-- Updated: Red Team + Validation Session 1 - D1–D6 điền theo ck-predict (owner ủy quyền 2026-09-30) -->
## Quyết định (đã chốt — dự đoán ck-predict được owner ủy quyền, 2026-09-30)
- **D1** SPDX: `GPL-3.0-only`.
- **D2** Bundle Oracle ODP.NET + Microsoft SNI runtime; thêm GPLv3 §7 additional permission theo mẫu FSF (faq GPLIncompatibleLibs) nêu đích danh Oracle ODP.NET, Microsoft SNI runtime, Visual Studio, VS Code + một mệnh đề chung cho driver CSDL không tương thích GPL và IDE host mà chương trình tương tác; **bỏ** mệnh đề tùy chọn "library source trong Corresponding Source". Kèm nguyên văn Oracle FDHUT và Microsoft SNI licence trong `docs/legal/THIRD-PARTY-NOTICES.md`, sao chép vào **mọi** kênh (bảng trên). Lập luận "aggregate" bị bác (single-file exe gọi API driver = một chương trình; SNI không phải System Library vì DataGuard phân phối nó). Phương án không bundle / tải driver theo yêu cầu / cờ `Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows` chỉ ghi làm điều kiện mở lại.
- **D3** `DataGuard.Contracts` thành gói permissive riêng: `PackageLicenseExpression=MIT` trong `DataGuard.Contracts.csproj` (ghi đè central GPL-3.0-only). Lý do: nằm trong binary closed-source của khách; reflection (`ManualContractSource.cs:33-83`) nên không dùng `[Conditional]` được.
- **D4** Analyzers/CodeFixes/SqlClassification/LanguageServer: mục tiêu gói analyzer chỉ build-time (`DevelopmentDependency=true`, không lib assets), `SqlClassification` nhúng trong analyzer + `IsPackable=false`, `LanguageServer` `IsPackable=false` (Exe trong VS Code extension). P2 điều tra tests-first trước khi lật `IncludeBuildOutput` (`DataGuard.Analyzers.csproj:11-14`; `CHANGELOG.md:57` "loads in Visual Studio"); nếu bị chặn → fallback: mở rộng đối xử permissive/exception cho các DLL đó và FAQ ghi "bất kỳ DLL DataGuard nào vào thư mục output của bạn đều được convey theo GPL trừ khi có giấy phép thương mại". README FAQ **không** được nói "không có nghĩa vụ phân phối" cho tới khi D4 xong.
- **D5** AGPL cho `DataGuard.Host`: không làm bây giờ (GPLv3 §13 cho phép thêm sau mà không relicense phần còn lại); điều kiện mở lại = có đối thủ SaaS hosted đáng tin.
- **D6** Metadata NuGet: central `PackageLicenseExpression=GPL-3.0-only` trong `Directory.Build.props` (xóa 6 dòng MIT per-project; Contracts ghi đè MIT); **không** `PackageLicenseFile`, **không** nhánh Y/Z, **không** file gộp nuget-license-text.txt (nuget.org từ chối `WITH LicenseRef-...` — probe HTTP 400; người nhận có thể bỏ §7 nên expression không sai). `Authors` + `Copyright` central; xóa 9 dòng `<Authors>DataGuard Contributors</Authors>` per-csproj. Header file nguồn: **không** (38/~129 .cs có header — ghi nhận không nhất quán, chấp nhận, YAGNI). Root: **không** file mới (không NOTICE/COMMERCIAL-LICENSE.md/CLA.md); `docs/legal/THIRD-PARTY-NOTICES.md` + `docs/legal/ADDITIONAL-PERMISSIONS.md` (hợp lệ theo `scripts/anti_garbage_guard.sh:7`); zero edit `anti_garbage_guard.sh`/`preflight_agent_check.sh`.
- **Điều kiện mở lại**: luật sư duyệt §7/commercial (V2), đối thủ SaaS (D5), nuget.org đổi chính sách expression, phương án không bundle driver.

## Kết quả RED
Chạy 2026-09-30 trên nhánh `feat/gpl3-dual-license` (Git Bash, Python 3.13, dotnet 10.0.401, gh đã đăng nhập).

| # | Lệnh (rút gọn) | Kết quả | Trạng thái |
|---|---|---|---|
| 1 | `python3 scripts/check-nuget-licences.py` | `checked 281 NuGet packages … and 8 production npm packages … OK`, exit 0 | GREEN (baseline giữ) |
| 2 | `dotnet list DataGuard.CrossPlatform.slnf package --include-transitive \| grep Oracle\|SNI` | `Oracle.ManagedDataAccess.Core 23.26.301` và `Microsoft.Data.SqlClient.SNI.runtime 7.1.0` đều xuất hiện | Xác nhận cột "Chứa Oracle/SNI" |
| 3 | `grep -inE reverse …/LICENSE.txt` (Oracle), `head` SNI | Oracle có "reverse engineering" (dòng 29); SNI là "MICROSOFT SOFTWARE LICENSE TERMS / MICROSOFT.DATA.SQLCLIENT.SNI LIBRARY" | Marker allow-list đúng |
| 4a | `unzip -l dataguard-0.3.0-win-x64.zip \| grep -ciE 'LICENSE\|THIRD-PARTY\|ADDITIONAL-PERMISSIONS'` | **0** | RED |
| 4b | `unzip -l DataGuard.Cli.0.3.0.nupkg \| grep -ciE 'LICENSE\|THIRD-PARTY'`; `<license` | **0**; `<license type="expression">MIT</license>` | RED |
| 4c | `unzip -l DataGuard.Cli.0.3.0.nupkg \| grep -ciE 'Oracle.ManagedDataAccess\|SNI'` | **4** (tool nupkg mang Oracle/SNI) | Xác nhận |
| 4d | VSIX nightly `DataGuard.VisualStudio-0.3.0-nightly.20260930.11.vsix`: `grep -ciE 'THIRD-PARTY\|ADDITIONAL'`; `grep LICENSE` | **0**; chỉ `LICENSE.txt` (1068 B, MIT) | RED |
| 4e | Docker: `docker run … ls /app` | daemon **không sẵn sàng** (npipe dockerDesktopLinuxEngine) → chạy lại ở P6 | Hoãn P6 |
| 4f | `docker buildx imagetools inspect …:0.3.0 \| grep image.licenses` | **`"org.opencontainers.image.licenses": "MIT"`** (2 lần) — KHÁC giả định plan (0) | **Phát hiện mới, xem dưới** |

**Phát hiện mới cần P2/P6 xử lý (ngoài plan):**
1. **Nhãn OCI `image.licenses` đã tồn tại và là `MIT`** — do `docker/metadata-action` (`release.yml:772`) tự sinh từ license repo GitHub, rồi `labels: ${{ steps.meta.outputs.labels }}` (`release.yml:794`) ghi đè `LABEL` trong Dockerfile. Hệ quả: chỉ thêm `LABEL … GPL-3.0-only` vào `Dockerfile:64-65` **không đủ** — build-push-action ưu tiên nhãn của metadata-action; sau khi đổi `LICENSE` nhãn tự động sẽ là `GPL-3.0` (SPDX id GitHub, không phải `GPL-3.0-only`). Cách sửa tối thiểu: thêm `labels: org.opencontainers.image.licenses=GPL-3.0-only` vào step metadata (đụng `release.yml` — cùng file P6 sửa guard; owner của `release.yml` = P6). Assert P6 #8 kiểm đúng `GPL-3.0-only`.
2. **SNI §3.a.ii/iii** (`LICENSE.txt` dòng 17-23): người phân phối phải "require distributors and external end users to agree to terms that protect it at least as much" và **không** được phân phối Distributable Code sao cho một phần của nó thành đối tượng "Excluded License" (copyleft đòi mở nguồn). SNI là object code, không sửa → GPL áp lên DataGuard không áp lên SNI; nhưng §7 additional permission + `THIRD-PARTY-NOTICES.md` phải nêu SNI giữ điều khoản riêng, nhãn "Separately Licensed". Đây là rủi ro pháp lý V2 (chưa qua luật sư), không chặn.
3. **Oracle** (`LICENSE.txt` dòng 16-29): điều kiện dòng 25 (kèm bản giấy phép mọi lần phân phối), 26 (không thu phí riêng cho driver — bán giấy phép thương mại DataGuard là "for-fee product … adds substantial additional value", được phép theo chính dòng 26), 27 (giữ nguyên notice), 29 (cấm reverse engineering); dòng 16/20 chỉ "unmodified Programs".
4. Lockfile: 18 `packages.lock.json` đang `M` trong working tree nhưng `git diff` rỗng (khác biệt CRLF/stat) — **WIP không liên quan**, không đưa vào commit licence; commit bằng đường dẫn tường minh, không `git add -A`.
5. Không build VSIX để lấy `obj/cli-publish.packages.lock.json` (bước 2): cần VS build tools, chậm; runtime pack theo RID nằm trong graph đã quét; P2 #3 phủ kênh VSIX. Ghi "not built".
6. `gh api repos/thanhnt-sm/eco_support_net_oracle/rulesets -q length` = **0** → điều kiện vào P6 bước 8/9 (ruleset tag) **chưa đạt**; việc của owner.

## Success Criteria
<!-- Updated: Red Team + Validation Session 1 - tiêu chí kiểm bằng lệnh; bỏ gate luật sư -->
- [x] `python3 scripts/check-nuget-licences.py` exit 0 (baseline).
- [x] Mục "Kết quả RED" có output của 4 assert artifact (zip, Cli nupkg, VSIX, Docker hoặc ghi "daemon không sẵn sàng" + lệnh để P6 chạy).
- [x] Bảng kênh có 6 dòng, mỗi dòng có `file:line` workflow tạo ra nó và một lệnh assert.
- [x] Trích nghĩa vụ Oracle/SNI (bước 4) có số dòng trong LICENSE.txt của gói.
- [x] D1–D6 ghi trong phase (đã có); không còn ô "____".

## Risk Assessment
| Rủi ro | Khả năng × Tác động | Giảm thiểu |
|---|---|---|
| Sót một kênh phân phối → kênh đó không có notice, vi phạm Oracle "licence copy must accompany" | TB × Cao | Bảng kênh liệt kê theo workflow `file:line`; P6 assert lại từng kênh trước tag |
| D4 lật `IncludeBuildOutput` phá analyzer trong VS | TB × TB | P2 tests-first + fallback FAQ đã định nghĩa |
| Kết luận pháp lý sai vì chưa có luật sư (V2, owner chấp nhận) | TB × Cao | README/FAQ nói thẳng chưa qua luật sư; mở lại khi có review |
| Rollback | — | Phase chỉ đọc; không cần rollback |

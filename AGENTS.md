# Sample Manager - Local Contract

## Scope

- Phase 1 only: create and read back sample requests from `YEU_CAU_MAU`.
- QA options come from `DM_QA` in the configured workbook.
- `QUAN_LY_MAU` and `BCTVL` are out of scope.
- Do not create OAuth material. Reuse the copied proven `.secrets` files.

## Entrypoint

- Source: `src\SampleManager\SampleManager.csproj`
- Runtime: `Sample Manager.exe`
- Build: MSBuild trực tiếp trên `src\SampleManager\SampleManager.csproj`
- Run: mở trực tiếp `Sample Manager.exe`

## Data contract

- Spreadsheet: `1mliaTlEftFXI3mqDYUny134MutVqjFPZBqrNKL424aQ`
- Request sheet: `YEU_CAU_MAU`
- QA sheet: `DM_QA`
- Request writes use the live header order and `values.append` with `RAW`.
- Every successful save is read back by exact `YeuCau_ID` before the UI reports success.

## Verification minimum

- Build the WinForms executable.
- Run the real UI journey: open app, open `Yêu cầu mẫu`, enter data, normalize, save, read back and display.
- Delete only the created test record after readback is observed.

## RELEASE — LOCAL FIRST

Authority:

- Khi Boss yêu cầu `release`, `publish`, `tạo bản update` hoặc tương đương, Luna mặc định chạy `tools\release.ps1` trên workspace local.
- Workspace local hiện tại là release authority.
- Không tạo version/release nếu Boss chưa yêu cầu.

Normal path:

`feature PASS → tools\release.ps1 → GitHub Release → clone E2E auto-update`

Chuẩn bị công cụ một lần:

- `gh auth status` phải PASS trước release.
- GitHub CLI dùng trực tiếp để tạo GitHub Release.
- Git dùng trực tiếp để commit/push source lên `main`.

GitHub connector, base64, blob/tree, `.release` staging và GitHub Actions không phải normal release path.

Workflow `.github/workflows/publish-release.yml` giữ nguyên như legacy fallback; chỉ đổi đường khi local release xuất hiện blocker thật và đã có evidence, necessity, return path rõ ràng.

## RELEASE / AUTO UPDATE — PROVEN FLOW

Authority:
- Boss yêu cầu release/update → Luna tự thực hiện toàn bộ trên máy local.
- SOL chỉ setup GitHub ban đầu hoặc hỗ trợ khi có blocker GitHub thật.
- Không cần SOL tham gia mỗi release thông thường.

Repo:
https://github.com/kingstorm1312-ai/Sample-Manager

Update manifest production:
https://github.com/kingstorm1312-ai/Sample-Manager/releases/latest/download/update.json

Proven E2E:
0.1.0 → Internet update → SHA PASS → updater → replace → restart → 0.1.1
Protected `.secrets` / OAuth token vẫn nguyên.

### Khi Boss nói “release”, “tạo bản update”, “publish bản mới” hoặc tương đương:

1. Xác định current version.
2. Bump version đúng bản Boss yêu cầu; nếu Boss chỉ nói release mới thì tăng patch version:
   x.y.z → x.y.(z+1)

3. Build app + updater:
   - 0 warning
   - 0 error

4. Tạo release package:
   `release/<version>/Sample.Manager-<version>-final.zip`

5. Package phải đúng structure proven hiện tại.
   Không tự thêm file/runtime khác nếu không có evidence cần.

6. Tính SHA-256 ZIP cuối.

7. Tạo:
   `release/<version>/update.json`

   Nội dung:
   - version
   - downloadUrl
   - sha256
   - mandatory=false
   - notes

   downloadUrl:
   https://github.com/kingstorm1312-ai/Sample-Manager/releases/download/v<version>/Sample.Manager-<version>-final.zip

8. Publish GitHub Release:
   tag:
   `v<version>`

   title:
   `Sample Manager <version>`

   assets:
   - `Sample.Manager-<version>-final.zip`
   - `update.json`

9. Verify release:
   - đúng tag
   - đủ 2 assets
   - ZIP SHA khớp local
   - `releases/latest/download/update.json` trả đúng version mới.

10. E2E update:
   - chạy một baseline version cũ;
   - verify:
     old
     → manifest mới
     → download Internet
     → SHA PASS
     → updater
     → process cũ exit
     → replace
     → restart
     → ProductVersion = version mới.

11. Verify:
   - `.secrets` còn nguyên
   - OAuth token còn nguyên
   - app mới chạy bình thường.

12. PASS → STOP.

### RULES

- Ưu tiên tool/local flow nhanh nhất đang proven trên máy Boss.
- Không hỏi SOL làm release thông thường.
- Không tạo repo mới.
- Không redesign updater.
- Không dùng chunk/base64 staging nếu đường publish trực tiếp hiện tại đang chạy.
- Không audit business logic/UI/Sheet khi release nếu không có evidence lỗi.
- Nếu release/update FAIL:
  chỉ đào sâu đúng edge fail.
- Không mở nhánh bên cạnh.
- Không cleanup/refactor “nhân tiện”.
- Protected state tuyệt đối không được đóng gói hoặc overwrite:
  `.secrets`, OAuth token, credentials, local auth state.
- Không commit secrets lên GitHub.

KNOWN PROVEN BASELINE:
Release v0.1.1:
https://github.com/kingstorm1312-ai/Sample-Manager/releases/tag/v0.1.1

Asset SHA:
ecf10270a3a3d9197797e28af3c8f479fba434e1e7f3213a9cf023f346017260

Đây là flow authority cho các release sau cho tới khi Boss thay đổi.

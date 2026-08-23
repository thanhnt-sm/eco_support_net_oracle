# `dataguard assess`

Read-only assessment của workspace .NET: inventory project/TFM/package format, legacy support status (curated table), lock-file consistency, SDK pinning, secret-like config values, machine-specific paths.

## Cú pháp

```bash
dataguard assess [--workspace <path>] [--project-filter <filter>...]
                 [--format text|json|sarif] [--output <file>] [--verbose]
```

## Options

| Option | Description | Default |
|---|---|---|
| `--workspace <path>` | Workspace root để assess | `.` |
| `--project-filter <filter>` | Lọc project theo substring path; lặp được | tất cả |
| `--format <format>` | `text` (mặc định, stdout tóm tắt), `json`, `sarif` | `text` |
| `--output <file>` | Bắt buộc khi `--format json\|sarif`; tool không bao giờ ghi machine-readable ra stdout | — |
| `--verbose` | In từng finding kèm evidence path | off |

## Report schema (JSON)

```json
{
  "schemaVersion": "1.0",
  "toolVersion": "<assembly version>",
  "target": "<workspace root>",
  "generatedAt": "<UTC ISO-8601>",
  "findings": [
    {
      "ruleId": "DG1103",
      "severity": 2,
      "confidence": 0,
      "message": "...",
      "evidence": [ { "path": "...", "key": "...", "line": null, "valuePreview": "..." } ],
      "suggestedAction": null,
      "appliesTo": ["net462"]
    }
  ],
  "errors": [ { "code": "DG1003", "path": "...", "message": "..." } ],
  "summary": { "critical": 0, "errors": 0, "warnings": 3, "information": 0, "toolErrors": 0 }
}
```

`severity`: 0 Critical, 1 Error, 2 Warning, 3 Information. `confidence`: 0 High, 1 Medium, 2 Low.

## Rule IDs

| Rule | Ý nghĩa |
|---|---|
| `DG1000` | Workspace root không tồn tại |
| `DG1001` | Path nằm ngoài requested workspace |
| `DG1002` | File không tìm thấy |
| `DG1003` | Project metadata hỏng (XML không parse được) |
| `DG1101` | TFM không có entry trong curated support table → status `Unknown`, không suy đoán |
| `DG1102` | TFM out of support (theo Microsoft lifecycle) |
| `DG1103` | TFM sắp hết support (có end date) |
| `DG1201` | Có PackageReference nhưng thiếu packages.lock.json |
| `DG1202` | Lock file không phủ TFM mà project khai báo |
| `DG1301` | Không có global.json pin SDK trong khi project yêu cầu |
| `DG1302` | global.json pin lệch với TFM major của projects |
| `DG1401` | Config value khớp key name giống secret; giá trị luôn bị redact |
| `DG1402` | Config chứa absolute path machine-specific |

## Ranh giới

- **Read-only tuyệt đối**: assess không bao giờ sửa solution/project/package/config/source.
- **Local-first**: mọi check chạy local; không có network call nào mặc định.
- **Fail-safe per-project**: một project hỏng sinh error entry, các sibling vẫn được assess.
- **Secrets**: finding chỉ chứa key name + `[redacted]`; giá trị thật không bao giờ xuất hiện trong output/log.
- Exit code: findings/tool errors → `1`; usage error → `2`; sạch → `0`.

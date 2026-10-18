# Marketplace omitted optional inputs

Base: ca68a3159425495f771da866188df75a8237ab4b. Separate branch fix/marketplace-optional-inputs; prior artifacts/worktrees preserved. User authorized source commit/push only; no merge, dispatch, settings or credential changes.

P1: hosted build-only run39132861675 failed at Record inspected package with `marketplace-release: 'release_tag'`. GitHub omitted blank optional inputs. validate_inputs used a default while checking the tag but returned the original mapping without the key; record_package then directly indexed it. Both hosted prepare jobs failed: VS Code job117691945251 step7 and Windows job117691945253 step6. Parent-confirmed Windows VSIX build/assert passed at version0.3.0 with26entries/8required before metadata failure; no artifacts/SBOM/attestation were uploaded and publication jobs were skipped. Other optional fields had the same noncanonical representation, although publication guards normally rejected missing selected fields before direct indexing.

Normalize all six optional strings once in validate_inputs on a copied mapping: release_tag, prepared_run_id, vscode_sha256, visualstudio_sha256, readiness_url, confirm. Missing becomes empty; explicit null/boolean/number/list/object is rejected rather than coerced. Keep publish strict boolean, required source/target validation, selected publication requirements, exact manifest matching and nonpromotion of untagged previews. No workflow YAML, product source, version, engine or license change.

Tests first reproduced the real record CLI failure for both synthetic VSCode and VS packages with omitted fields, while explicit empty fields recorded successfully. Added source/verify entrypoint coverage, normalization equivalence/input immutability, malformed optional-value matrix, omitted/blank selected publication-field rejection and omitted unselected hash acceptance. Existing untagged-preview rejection remains. Focused21tests pass after correction. Full gates and independent review are recorded in local evidence.

EN/VI contract mapping: docs/marketplace-automation.md and .vi.md; implementation scripts/marketplace_release.py; regression scripts/tests/test_marketplace_release.py.

Tiếng Việt: Lỗi do GitHub bỏ trường tùy chọn trống, trong khi record truy cập release_tag trực tiếp. Chuẩn hóa một lần tại ranh giới validation trên bản sao input, không biến giá trị sai kiểu thành dữ liệu hợp lệ. Giữ nguyên điều kiện công bố và cấm nâng preview chưa tag; không thay đổi setting, credential hay phiên bản sản phẩm.

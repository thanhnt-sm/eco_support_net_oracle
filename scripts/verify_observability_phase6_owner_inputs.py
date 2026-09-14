#!/usr/bin/env python3
"""Fail-closed validation for the redacted Phase 6B owner input packet.

The validator intentionally accepts references to secrets and endpoints, never
their values. ``--template`` checks the repository example without authorizing
runtime work; ``--file`` requires an owner-approved packet and rejects
placeholders. No Kubernetes, backend or profiler API is contacted.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any, NoReturn


ROOT = Path(__file__).resolve().parents[1]
SCHEMA = ROOT / "docs/observability/phase6-owner-inputs.schema.json"
REF_RE = re.compile(r"^(?:ref|ticket)://[a-z0-9][a-z0-9._/-]{1,190}(?:#[a-z0-9._-]+)?$")
SEMVER_RE = re.compile(
    r"^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$"
)
PACKET_ID_RE = re.compile(r"^phase6-[a-z0-9][a-z0-9.-]{1,62}$")
NAMESPACE_RE = re.compile(r"^[a-z0-9](?:[-a-z0-9]*[a-z0-9])?$|^[a-z0-9]$")
API_VERSION_RE = re.compile(r"^v[0-9]+(?:\.[0-9]+){0,2}$")
FORBIDDEN_KEY_RE = re.compile(
    r"(?:^|_)(?:password|passwd|access_token|refresh_token|authorization|cookie|"
    r"private_key|client_secret|token_value|pan|cvv|account_number|customer_id|"
    r"raw_payload|message_payload|request_body|response_body|sql_parameter|"
    r"redis_value|method_argument|return_value)(?:$|_)"
)
BEARER_RE = re.compile(r"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{12,}")
# JWTs conventionally use an ``eyJ`` JSON header. Requiring that prefix avoids
# treating ordinary dotted package names such as ``Grpc.Net.Client`` as secrets.
JWT_RE = re.compile(r"^eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$")
PAN_RE = re.compile(r"^(?:\d[ -]?){13,19}$")
PLACEHOLDER_RE = re.compile(r"(?i)(?:replace(?:-me)?|example\.invalid|<[^>]+>)")
SECRET_MARKER_RE = re.compile(
    r"(?i)\b(?:password|passcode|api[_-]?key|secret(?:[ _-]?(?:value|key))?|"
    r"access[_-]?token|refresh[_-]?token|authorization|cookie|cvv|pan)\b"
)
BACKEND_PROTOCOLS = {
    "loki": {"otlp-http"},
    "tempo": {"otlp-http", "otlp-grpc"},
    "mimir": {"otlp-http", "prometheus-remote-write"},
    "pyroscope": {"pyroscope-http"},
}


class ValidationError(ValueError):
    """A packet contract violation with a stable human-readable path."""


def fail(message: str) -> NoReturn:
    print(f"[phase6-owner-inputs] ERROR: {message}", file=sys.stderr)
    raise SystemExit(2)


def require_object(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ValidationError(f"{path} must be an object")
    return value


def require_keys(value: dict[str, Any], path: str, expected: set[str]) -> None:
    missing = sorted(expected - value.keys())
    extra = sorted(value.keys() - expected)
    if missing:
        raise ValidationError(f"{path} is missing keys: {', '.join(missing)}")
    if extra:
        raise ValidationError(f"{path} has unsupported keys: {', '.join(extra)}")


def require_string(value: Any, path: str, *, max_length: int = 192) -> str:
    if not isinstance(value, str) or not value or len(value) > max_length:
        raise ValidationError(f"{path} must be a non-empty string of at most {max_length} characters")
    if any(ord(character) < 0x20 or ord(character) == 0x7F for character in value):
        raise ValidationError(f"{path} contains a control character")
    return value


def require_enum(value: Any, path: str, allowed: set[str]) -> str:
    text = require_string(value, path)
    if text not in allowed:
        raise ValidationError(f"{path} must be one of: {', '.join(sorted(allowed))}")
    return text


def require_ref(value: Any, path: str, *, ticket_only: bool = False) -> str:
    text = require_string(value, path)
    if ticket_only:
        if not re.fullmatch(r"ticket://[a-z0-9][a-z0-9._/-]{1,190}", text):
            raise ValidationError(f"{path} must be a ticket:// reference, not a value")
    elif not REF_RE.fullmatch(text):
        raise ValidationError(f"{path} must be a ref:// or ticket:// reference, not a value")
    return text


def require_number(
    value: Any,
    path: str,
    *,
    minimum: float,
    maximum: float | None = None,
    exclusive_minimum: bool = False,
) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValidationError(f"{path} must be a number")
    number = float(value)
    if number != number or number in (float("inf"), float("-inf")):
        raise ValidationError(f"{path} must be finite")
    if number <= minimum if exclusive_minimum else number < minimum:
        raise ValidationError(f"{path} must be greater than {minimum}")
    if maximum is not None and number > maximum:
        raise ValidationError(f"{path} must be at most {maximum}")
    return number


def require_integer(value: Any, path: str, *, minimum: int, maximum: int | None = None) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise ValidationError(f"{path} must be an integer")
    if value < minimum or (maximum is not None and value > maximum):
        bound = f"{minimum}..{maximum}" if maximum is not None else f">={minimum}"
        raise ValidationError(f"{path} must be in {bound}")
    return value


def walk_forbidden(value: Any, path: str = "$", *, reject_placeholders: bool = False) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            if FORBIDDEN_KEY_RE.search(key):
                raise ValidationError(f"{path}.{key} is a prohibited secret or payload field")
            walk_forbidden(child, f"{path}.{key}", reject_placeholders=reject_placeholders)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            walk_forbidden(child, f"{path}[{index}]", reject_placeholders=reject_placeholders)
    elif isinstance(value, str):
        if BEARER_RE.search(value) or JWT_RE.fullmatch(value) or PAN_RE.fullmatch(value):
            raise ValidationError(f"{path} looks like a credential or payment-card value")
        if not REF_RE.fullmatch(value) and SECRET_MARKER_RE.search(value):
            raise ValidationError(f"{path} contains a credential marker; use a reference instead")
        if reject_placeholders and PLACEHOLDER_RE.search(value):
            raise ValidationError(f"{path} still contains a placeholder")


def validate_owner(packet: dict[str, Any], *, approved_required: bool, reject_placeholders: bool) -> None:
    require_keys(
        packet,
        "$",
        {
            "schema_version",
            "packet_id",
            "approved",
            "environment",
            "owner",
            "kubernetes",
            "clients",
            "backends",
            "traffic",
            "slos",
            "profiler",
        },
    )
    if packet["schema_version"] != "1.0":
        raise ValidationError("$.schema_version must be exactly 1.0")
    packet_id = require_string(packet["packet_id"], "$.packet_id", max_length=64)
    if not PACKET_ID_RE.fullmatch(packet_id):
        raise ValidationError("$.packet_id must use the phase6-<stable-name> form")
    if not isinstance(packet["approved"], bool):
        raise ValidationError("$.approved must be boolean")
    if approved_required and packet["approved"] is not True:
        raise ValidationError("$.approved must be true for an owner-approved packet")
    require_enum(packet["environment"], "$.environment", {"integration", "synthetic-canary", "canary"})

    owner = require_object(packet["owner"], "$.owner")
    require_keys(owner, "$.owner", {"team", "owner_ref", "approval_ref", "reviewed_at"})
    require_string(owner["team"], "$.owner.team")
    require_ref(owner["owner_ref"], "$.owner.owner_ref")
    require_ref(owner["approval_ref"], "$.owner.approval_ref", ticket_only=True)
    reviewed_at = require_string(owner["reviewed_at"], "$.owner.reviewed_at")
    try:
        parsed_reviewed_at = datetime.fromisoformat(reviewed_at.replace("Z", "+00:00"))
    except ValueError as error:
        raise ValidationError("$.owner.reviewed_at must be an ISO-8601 timestamp") from error
    if "T" not in reviewed_at or parsed_reviewed_at.tzinfo is None:
        raise ValidationError("$.owner.reviewed_at must include a time and timezone")

    kubernetes = require_object(packet["kubernetes"], "$.kubernetes")
    require_keys(
        kubernetes,
        "$.kubernetes",
        {
            "context_ref",
            "api_version",
            "cni",
            "namespace",
            "namespace_labels",
            "workload_identity",
            "server_dry_run_approved",
        },
    )
    require_ref(kubernetes["context_ref"], "$.kubernetes.context_ref")
    api_version = require_string(kubernetes["api_version"], "$.kubernetes.api_version", max_length=32)
    if not API_VERSION_RE.fullmatch(api_version):
        raise ValidationError("$.kubernetes.api_version must look like v1.30")
    require_string(kubernetes["cni"], "$.kubernetes.cni")
    namespace = require_string(kubernetes["namespace"], "$.kubernetes.namespace", max_length=63)
    if not NAMESPACE_RE.fullmatch(namespace):
        raise ValidationError("$.kubernetes.namespace is not a DNS label")
    labels = require_object(kubernetes["namespace_labels"], "$.kubernetes.namespace_labels")
    if not 1 <= len(labels) <= 32:
        raise ValidationError("$.kubernetes.namespace_labels must contain 1..32 labels")
    for key, value in labels.items():
        require_string(key, "$.kubernetes.namespace_labels key", max_length=63)
        require_string(value, f"$.kubernetes.namespace_labels.{key}")
    require_ref(kubernetes["workload_identity"], "$.kubernetes.workload_identity")
    if not isinstance(kubernetes["server_dry_run_approved"], bool):
        raise ValidationError("$.kubernetes.server_dry_run_approved must be boolean")
    if approved_required and kubernetes["server_dry_run_approved"] is not True:
        raise ValidationError("$.kubernetes.server_dry_run_approved must be true")

    clients = packet["clients"]
    if not isinstance(clients, list) or len(clients) != 5:
        raise ValidationError("$.clients must contain exactly grpc, kafka, rabbitmq, npgsql and redis")
    expected_components = {"grpc", "kafka", "rabbitmq", "npgsql", "redis"}
    seen_components: set[str] = set()
    for index, raw_client in enumerate(clients):
        path = f"$.clients[{index}]"
        client = require_object(raw_client, path)
        require_keys(
            client,
            path,
            {
                "component",
                "status",
                "package",
                "version",
                "retry_policy",
                "dlq_policy",
                "streaming_policy",
                "duplicate_policy",
                "idempotency_policy",
            },
        )
        component = require_enum(client["component"], f"{path}.component", expected_components)
        if component in seen_components:
            raise ValidationError(f"duplicate client component: {component}")
        seen_components.add(component)
        status = require_enum(client["status"], f"{path}.status", {"used", "not_used"})
        package = require_string(client["package"], f"{path}.package")
        version = require_string(client["version"], f"{path}.version")
        if status == "used":
            if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{1,127}", package):
                raise ValidationError(f"{path}.package is not a package identifier")
            if not SEMVER_RE.fullmatch(version):
                raise ValidationError(f"{path}.version must be an exact semantic version")
        elif package != "not-used" or version != "not-used":
            raise ValidationError(f"{path}.package/version must be not-used when status is not_used")
        for field in ("retry_policy", "dlq_policy", "streaming_policy", "duplicate_policy", "idempotency_policy"):
            require_string(client[field], f"{path}.{field}")
    if seen_components != expected_components:
        raise ValidationError("$.clients must cover every required component exactly once")

    backends = require_object(packet["backends"], "$.backends")
    require_keys(backends, "$.backends", {"loki", "tempo", "mimir", "pyroscope"})
    for backend_name, raw_backend in backends.items():
        path = f"$.backends.{backend_name}"
        backend = require_object(raw_backend, path)
        require_keys(
            backend,
            path,
            {"status", "protocol", "endpoint_ref", "tenant_ref", "tls_mode", "residency", "retention_days"},
        )
        status = require_enum(backend["status"], f"{path}.status", {"enabled", "disabled"})
        protocol = require_enum(
            backend["protocol"],
            f"{path}.protocol",
            {"otlp-http", "otlp-grpc", "prometheus-remote-write", "pyroscope-http", "not-used"},
        )
        endpoint_ref = require_string(backend["endpoint_ref"], f"{path}.endpoint_ref")
        tenant_ref = require_string(backend["tenant_ref"], f"{path}.tenant_ref")
        tls_mode = require_enum(backend["tls_mode"], f"{path}.tls_mode", {"tls", "mtls", "not-used"})
        residency = require_string(backend["residency"], f"{path}.residency")
        retention = require_integer(backend["retention_days"], f"{path}.retention_days", minimum=0, maximum=3650)
        if status == "enabled":
            if protocol == "not-used":
                raise ValidationError(f"{path}.protocol is required when backend is enabled")
            if protocol not in BACKEND_PROTOCOLS[backend_name]:
                allowed_protocols = ", ".join(sorted(BACKEND_PROTOCOLS[backend_name]))
                raise ValidationError(f"{path}.protocol must be one of: {allowed_protocols}")
            require_ref(endpoint_ref, f"{path}.endpoint_ref")
            require_ref(tenant_ref, f"{path}.tenant_ref")
            if tls_mode == "not-used" or retention < 1:
                raise ValidationError(f"{path} requires TLS/mTLS and positive retention when enabled")
        elif (protocol, endpoint_ref, tenant_ref, tls_mode, residency, retention) != (
            "not-used",
            "not-used",
            "not-used",
            "not-used",
            "not-used",
            0,
        ):
            raise ValidationError(f"{path} must use not-used/0 fields when disabled")

    traffic = require_object(packet["traffic"], "$.traffic")
    require_keys(
        traffic,
        "$.traffic",
        {
            "average_rps",
            "peak_rps",
            "average_payload_bytes",
            "max_payload_bytes",
            "telemetry_outage_tolerance_seconds",
            "rto_seconds",
            "rpo_seconds",
        },
    )
    average_rps = require_number(traffic["average_rps"], "$.traffic.average_rps", minimum=0, exclusive_minimum=True)
    peak_rps = require_number(traffic["peak_rps"], "$.traffic.peak_rps", minimum=0, exclusive_minimum=True)
    if peak_rps < average_rps:
        raise ValidationError("$.traffic.peak_rps must be >= average_rps")
    average_payload = require_integer(traffic["average_payload_bytes"], "$.traffic.average_payload_bytes", minimum=1, maximum=104857600)
    max_payload = require_integer(traffic["max_payload_bytes"], "$.traffic.max_payload_bytes", minimum=1, maximum=104857600)
    if max_payload < average_payload:
        raise ValidationError("$.traffic.max_payload_bytes must be >= average_payload_bytes")
    require_integer(
        traffic["telemetry_outage_tolerance_seconds"],
        "$.traffic.telemetry_outage_tolerance_seconds",
        minimum=1,
        maximum=604800,
    )
    require_integer(traffic["rto_seconds"], "$.traffic.rto_seconds", minimum=0, maximum=604800)
    require_integer(traffic["rpo_seconds"], "$.traffic.rpo_seconds", minimum=0, maximum=604800)

    slos = packet["slos"]
    if not isinstance(slos, list) or not 1 <= len(slos) <= 32:
        raise ValidationError("$.slos must contain 1..32 SLOs")
    for index, raw_slo in enumerate(slos):
        path = f"$.slos[{index}]"
        slo = require_object(raw_slo, path)
        require_keys(slo, path, {"name", "owner_ref", "target", "window", "minimum_valid_events"})
        name = require_string(slo["name"], f"{path}.name", max_length=96)
        if not re.fullmatch(r"[a-z][a-z0-9._-]{2,95}", name):
            raise ValidationError(f"{path}.name must be a stable SLO name")
        require_ref(slo["owner_ref"], f"{path}.owner_ref")
        require_number(slo["target"], f"{path}.target", minimum=0, maximum=1, exclusive_minimum=True)
        require_enum(slo["window"], f"{path}.window", {"7d", "28d", "30d"})
        require_integer(slo["minimum_valid_events"], f"{path}.minimum_valid_events", minimum=1, maximum=1000000000)

    profiler = require_object(packet["profiler"], "$.profiler")
    require_keys(
        profiler,
        "$.profiler",
        {"mode", "runtime", "os_arch", "symbol_policy", "overhead_budget_percent", "kill_switch_ref"},
    )
    mode = require_enum(profiler["mode"], "$.profiler.mode", {"disabled", "native-dotnet", "ebpf"})
    runtime = require_string(profiler["runtime"], "$.profiler.runtime")
    os_arch = require_string(profiler["os_arch"], "$.profiler.os_arch")
    symbol_policy = require_enum(
        profiler["symbol_policy"],
        "$.profiler.symbol_policy",
        {"none", "private-symbols", "redacted-source-map"},
    )
    overhead = require_number(profiler["overhead_budget_percent"], "$.profiler.overhead_budget_percent", minimum=0, maximum=20)
    require_ref(profiler["kill_switch_ref"], "$.profiler.kill_switch_ref")
    if mode == "disabled":
        if (runtime, os_arch, symbol_policy, overhead) != ("not-used", "not-used", "none", 0):
            raise ValidationError("disabled profiler must use not-used/none/0 fields")
    elif runtime == "not-used" or os_arch == "not-used" or symbol_policy == "none" or overhead <= 0:
        raise ValidationError("enabled profiler requires runtime, OS/architecture, symbols and a positive budget")

    walk_forbidden(packet, reject_placeholders=reject_placeholders)


def load_json(path: Path) -> Any:
    try:
        with path.open(encoding="utf-8") as stream:
            return json.load(stream)
    except FileNotFoundError:
        fail(f"file does not exist: {path}")
    except json.JSONDecodeError as error:
        fail(f"invalid JSON in {path}: line {error.lineno}, column {error.colno}")
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--template", type=Path, help="validate a redacted template without authorizing runtime")
    mode.add_argument("--file", type=Path, help="validate an owner-approved packet")
    args = parser.parse_args()

    schema = load_json(SCHEMA)
    if not isinstance(schema, dict) or schema.get("$id") != "https://schemas.dataguard.example/observability/phase6-owner-inputs.schema.json":
        fail("owner-input schema is missing or has an unexpected $id")
    packet_path = args.template or args.file
    packet = load_json(packet_path)
    if not isinstance(packet, dict):
        fail("owner input packet must be a JSON object")
    try:
        validate_owner(packet, approved_required=args.file is not None, reject_placeholders=args.file is not None)
    except ValidationError as error:
        fail(str(error))
    if args.template is not None:
        print("[phase6-owner-inputs] TEMPLATE_STATUS=PASS")
        print("[phase6-owner-inputs] RUNTIME_AUTHORIZATION=OWNER_REQUIRED")
    else:
        print("[phase6-owner-inputs] PACKET_STATUS=PASS")
        print("[phase6-owner-inputs] RUNTIME_AUTHORIZATION=APPROVED")
    print("[phase6-owner-inputs] No Kubernetes, backend, Secret or profiler API was contacted.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

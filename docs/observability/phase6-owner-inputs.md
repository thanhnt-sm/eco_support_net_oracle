# Phase 6B owner-input packet

Phase 6B cannot start from repository defaults. The owner supplies a redacted JSON packet that
binds the generic reference artifacts to a real cluster, workload and backend contract. The
packet is evidence metadata, not a secret store: endpoint, tenant, workload-identity and
kill-switch values are `ref://`/`ticket://` references only.

## Files and commands

- Schema: `phase6-owner-inputs.schema.json` (JSON Schema 2020-12, exact `1.0` contract).
- Template: `phase6-owner-inputs.example.json` (`approved=false`; never an approval).
- Validator: `scripts/verify_observability_phase6_owner_inputs.py`.

Validate the checked-in template without authorizing runtime work:

```sh
python3 scripts/verify_observability_phase6_owner_inputs.py \
  --template docs/observability/phase6-owner-inputs.example.json
```

Validate an owner packet before the Kubernetes gate:

```sh
python3 scripts/verify_observability_phase6_owner_inputs.py \
  --file /path/to/owner-inputs.json
```

The approved mode requires both `approved=true` and
`kubernetes.server_dry_run_approved=true`. It performs no cluster, backend, Secret or profiler
request. A later `kubectl apply --dry-run=server --validate=strict` remains mandatory.

## Required evidence

| Section | Required contract | Privacy boundary |
|---|---|---|
| `owner` | Team/reference, ticket reference and timezone-qualified review timestamp | No email, token or free-form credential |
| `kubernetes` | Context reference, API/CNI version, namespace labels, workload identity and dry-run approval | No kubeconfig or certificate value |
| `clients` | Exactly `grpc`, `kafka`, `rabbitmq`, `npgsql`, `redis`; exact semantic package version when used; explicit retry/DLQ/streaming/duplicate/idempotency policies | No payload, message key or connection string |
| `backends` | Loki `otlp-http`, Tempo `otlp-http`/`otlp-grpc`, Mimir `otlp-http`/`prometheus-remote-write`, Pyroscope `pyroscope-http`; TLS/mTLS, tenant reference, residency and retention | No endpoint, tenant credential or CA/private-key value |
| `traffic` | Average/peak RPS, payload bounds, telemetry outage tolerance and RTO/RPO | No request sample or business identifier |
| `slos` | Stable SLO name, owner reference, target, window and minimum valid events | No customer/account/transaction ID |
| `profiler` | Disabled/native .NET/eBPF choice, runtime/OS/arch, symbol policy, overhead budget and kill-switch reference | No symbols, source path or profile payload |

The validator rejects unsupported fields, credential markers, PAN/JWT/Bearer-shaped values,
placeholders and disabled sections that contain live-looking references. It also rejects
duplicate/missing client components, invalid backend protocol combinations and non-finite or
inverted traffic values.

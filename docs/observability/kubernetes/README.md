# Kubernetes deployment contract

The reference overlay is under this directory and uses the pinned Collector manifest
digest `sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6`
(`otel/opentelemetry-collector-contrib:0.160.0`). It is deliberately not applied to a
cluster by this change: discovery has no approved cluster, namespace, workload identity,
ingress, TLS, RBAC or resource budgets. Before activation, the platform owner must provide:

- immutable Collector distribution image digest and supported component set;
- service account, NetworkPolicy and egress destinations;
- mTLS certificate/secret references and rotation mechanism;
- CPU/memory requests, HPA/PDB and queue persistence requirements;
- tenant routing, data residency and retention configuration;
- a review of the broad namespace ingress rules in `network-policy.yaml`, narrowed to the
  application namespaces and backend namespaces actually in scope.

Required objects are Deployment/DaemonSet for agent and Deployment/Service for gateway, ConfigMap, Secret references, ServiceAccount/RBAC, NetworkPolicy, PDB and HPA. The agent ClusterRole is read-only and limited to the metadata resources required by `k8sattributes`; the gateway Role is read-only for service/endpoints discovery. Validation command: `kubectl apply --dry-run=server -f <overlay>` against the target cluster plus the exact Collector `validate` command. Status: `NOT EXECUTED`.

The Kustomize entry point is `kustomization.yaml`. It generates the two config maps from
the deployment copies under `config/` and sets `namespace: observability`; it does not create
a Secret and therefore cannot accidentally commit credentials. The copies are required
because Kustomize's default load restriction rejects `../collector/*.yaml`; keep them
byte-equivalent and compare them with `cmp` before release. Required Secret keys are:

- `otel-gateway-client-tls`: `ca.crt`, `tls.crt`, `tls.key` (agent → gateway mTLS);
- `otel-gateway-server-tls`: `ca.crt`, `tls.crt`, `tls.key` (gateway OTLP receiver mTLS; the
  CA must issue the agent client certificate);
- `otel-agent-server-tls`: `tls.crt`, `tls.key` (application → agent TLS; certificate SANs must
  cover the selected local-agent Service/host names);
- `otel-backend-tls`: `ca.crt` (gateway → Tempo server trust; an owner overlay may add a client
  keypair and exporter mTLS fields if the Tempo deployment requires client authentication);
- `grafana-backends`: `loki-otlp-endpoint`, `mimir-otlp-endpoint`, `tenant-id`.

Before apply, label only approved namespaces (for example
`observability.data-ingest=true` for application namespaces,
`observability.metrics-scrape=true` for the Prometheus scraper namespace and
`observability.backend-access=true` for Loki/Mimir namespaces). The policies intentionally
do not allow all namespaces: gateway OTLP ingress is restricted to agent pods in the
`observability` namespace, while agent ingress is restricted to explicitly labelled application
namespaces. The platform owner must add a narrow rule for the Kubernetes
API endpoint (and supply the control-plane CIDR) if the chosen CNI enforces egress to control-plane IPs.

Host-side YAML parsing passed with Ruby's Psych parser. `kubectl kustomize` rendering and
server-side validation are separate gates; local rendering is covered by the exact commands
below, while server-side validation is `NOT EXECUTED` without a target cluster. Before requesting
owner access, run the non-mutating Phase 6A preflight from the repository root:

```sh
./scripts/verify_observability_phase6_preflight.sh
```

Then ask the platform/service owner to provide only the redacted Phase 6B packet and validate it
without contacting the cluster:

```sh
python3 scripts/verify_observability_phase6_owner_inputs.py --file /path/to/owner-inputs.json
```

The packet must use secret/endpoint references and explicit approval; the checked-in
`../phase6-owner-inputs.example.json` is template-only.

The preflight validates the exact Collector image, component inventory, both configuration
copies, Prometheus rules, locked .NET restore/build/tests and dependency advisories. It never
calls `kubectl apply`, reads a Secret or contacts an LGTM backend; only the owner-gated Phase 6B
commands below can satisfy the server-side admission and canary requirements.

```sh
cmp docs/observability/collector/agent.yaml docs/observability/kubernetes/config/agent.yaml
cmp docs/observability/collector/gateway.yaml docs/observability/kubernetes/config/gateway.yaml
kubectl kustomize docs/observability/kubernetes
kubectl apply --dry-run=server --validate=strict -k docs/observability/kubernetes
```

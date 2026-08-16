use eco_agents::{DocBridgeAgent, PatchSynthesizerAgent, TriageAgent};
use eco_core::Config;

#[tokio::test]
async fn test_rust_triage_agent() {
    let agent = TriageAgent::new(Config::default());
    let res = agent
        .triage_issue(
            "py-simd/tokenizer",
            "42",
            "Segfault on thread exit",
            "Py_DECREF segfaults on worker thread exit without GIL",
            2048,
        )
        .await
        .expect("Triage should succeed");

    assert_eq!(res.repo, "py-simd/tokenizer");
    assert_eq!(res.issue_id, "42");
    assert!(!res.formatted_maintainer_reply.is_empty());
}

#[tokio::test]
async fn test_rust_patch_synthesizer() {
    let agent = PatchSynthesizerAgent::new(Config::default());
    let res = agent
        .synthesize_patch(
            "esoteric/cffi-tensor",
            "Null pointer deref when tensor shape is empty",
            "int get_dim(Tensor* t) { return t->shape[0]; }",
            4096,
        )
        .await
        .expect("Patch synthesis should succeed");

    assert_eq!(res.repo, "esoteric/cffi-tensor");
    assert!(res.safety_audit_passed);
}

#[tokio::test]
async fn test_rust_doc_bridge_agent() {
    let agent = DocBridgeAgent::new(Config::default());
    let res = agent
        .generate_mcp_bridge("custom-raster-io", "read_band(path, id)", 2048)
        .await
        .expect("Bridge generation should succeed");

    assert_eq!(res.package_name, "custom-raster-io");
    assert!(res.server_filename.contains("custom_raster_io_mcp"));
}

use eco_core::{ClaudeClient, Config};

#[tokio::test]
async fn test_claude_client_simulation_mode() {
    let mut config = Config::default();
    config.anthropic_api_key = None;
    config.thinking_budget_tokens = 2048;

    let client = ClaudeClient::new(config);
    assert!(!client.is_live());

    let res = client
        .generate_with_thinking("Analyze FFI memory boundary bug", None, Some(2048))
        .await
        .expect("Simulation should succeed");

    assert!(res.thinking.is_some());
    assert!(res.thinking.unwrap().contains("Simulation"));
    assert_eq!(res.usage.thinking_tokens, 2048);
    assert!(!res.content.is_empty());
}

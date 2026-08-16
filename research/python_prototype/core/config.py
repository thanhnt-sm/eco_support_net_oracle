"""Configuration management using Pydantic Settings."""

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Application settings loaded from environment or .env file."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # Anthropic Settings
    anthropic_api_key: str | None = Field(default=None, alias="ANTHROPIC_API_KEY")
    anthropic_model: str = Field(default="claude-3-7-sonnet-20250219", alias="ANTHROPIC_MODEL")
    thinking_budget_tokens: int = Field(default=4096, alias="THINKING_BUDGET_TOKENS")
    max_tokens: int = Field(default=20000, alias="MAX_TOKENS")

    # GitHub Settings
    github_token: str | None = Field(default=None, alias="GITHUB_TOKEN")

    # MCP Server Settings
    mcp_server_host: str = Field(default="127.0.0.1", alias="MCP_SERVER_HOST")
    mcp_server_port: int = Field(default=8000, alias="MCP_SERVER_PORT")

    # Observability
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")
    enable_telemetry: bool = Field(default=True, alias="ENABLE_TELEMETRY")


@lru_cache
def get_settings() -> Settings:
    """Singleton getter for application settings."""
    return Settings()

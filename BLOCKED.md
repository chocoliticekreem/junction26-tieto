# Blocked items for Anson

## 1. `.env.example` could not be written by the agent
The global guard hook (`~/.claude/scripts/global-guard.py`) blocks any write to a file whose name starts with `.env`, including the example file, and blocks shell commands that mention it. I did not route around a security hook. Create it yourself; it holds no secrets:

```
printf 'ANTHROPIC_API_KEY=\nJEV_API_KEY=\nMOCK_LLM=0\nDOTNET=~/.dotnet/dotnet\n' > .env.example
```

The pipeline does not need this file; it reads `.env.local`.

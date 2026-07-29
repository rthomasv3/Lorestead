Lorestead is built to be used by agents, not just read by them. It speaks the Model Context Protocol, so any MCP host - Claude Code, Claude Desktop, Codex, OpenCode, and others - can read and write your notes with the same tools.

There are two ways in. Both offer the same tools.

## Local, no server

A binary named `Lorestead.Mcp` ships inside every install and talks straight to the database on this machine. Where it lives depends on your platform:

- **Windows:** `%LocalAppData%\Lorestead\current\Lorestead.Mcp.exe` - the `current` folder keeps the same path across updates.
- **macOS:** `/Applications/Lorestead.app/Contents/MacOS/Lorestead.Mcp`
- **Linux:** the AppImage itself is the path. Run `Lorestead.AppImage --mcp` and it starts the MCP server instead of the app.

It finds the database the same way the app does. Set `LORESTEAD_DATA_DIR` if you keep yours somewhere unusual. Edits show up in the app within a second or two, no restart needed.

**Claude Code** - one command:

```
claude mcp add lorestead -- <path from above>
```

**Claude Desktop** - add the server to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "lorestead": {
      "command": "C:\\Users\\you\\AppData\\Local\\Lorestead\\current\\Lorestead.Mcp.exe"
    }
  }
}
```

**Codex** - add it to `~/.codex/config.toml`:

```toml
[mcp_servers.lorestead]
command = "<path from above>"
```

**OpenCode** - add it to `opencode.json`:

```json
{
  "mcp": {
    "lorestead": {
      "type": "local",
      "command": ["<path from above>"]
    }
  }
}
```

Each host's own docs cover the rest: [Claude Code](https://docs.claude.com/en/docs/claude-code/mcp), [Claude Desktop](https://modelcontextprotocol.io/quickstart/user), [Codex](https://learn.chatgpt.com/docs/extend/mcp), [OpenCode](https://opencode.ai/docs/mcp-servers).

## Over your server

If you run the sync server, it already serves MCP at `/mcp`. Use this when the agent is not on the machine that holds your notes - a hosted assistant, a CI job, your phone.

- Hosts that can send an `Authorization` header use your deployment's bearer token.
- Claude on the web and Claude's mobile apps cannot send one. They sign in with OAuth instead: add a custom connector pointing at `https://your-server/mcp` and enter your OAuth client id and secret in the connector's advanced settings.

You set both up on the server - [Sync Setup](note://019b76da-a800-7000-8000-000000000006) covers them.

## What an agent can do

Search, walk the note tree, list what changed recently, read a note or task in full, create and update notes, append to a note, make templates and new notes from them, work with boards and tasks, link a note to a task, and read or add attachments.

Search and listings return ids, titles, and snippets rather than whole bodies, so an agent finds the right note before spending its context on it.

## What an agent cannot do

Nothing destructive. There is no delete, no trash, no purge, no restore, no moving notes around the tree, and no removing attachments or links. Those stay with you.

Every agent edit lands in the same history as your own, under the agent's device name with `-mcp` on the end. Open a note's history and you can see exactly what an agent changed - and put the note back the way it was.

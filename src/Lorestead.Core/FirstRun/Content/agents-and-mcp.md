Lorestead is built to be used by agents, not just to be readable by them. It speaks the Model Context Protocol, so any MCP-capable host - Claude Code, Claude Desktop, and others - can read and write your notes with the same tools.

There are two ways in, offering the identical tool set.

## Local, no server

A small binary called `Lorestead.Mcp` ships inside this install and talks straight to the database on this machine. Point your agent host at its path and there is nothing else to configure:

```json
{
  "mcpServers": {
    "lorestead": {
      "command": "C:\\path\\to\\Lorestead.Mcp.exe"
    }
  }
}
```

It finds the database the same way the app does. Set `LORESTEAD_DATA_DIR` if you keep yours somewhere unusual. Edits show up in the app within a second or two, without a restart.

## Over your server

If you are running the sync server, it already exposes MCP at `/mcp` behind the same bearer token as the rest of the API. Use this when the agent is not on the same machine as your notes - a hosted assistant, a CI job, a phone.

## What an agent can do

Search, walk the note tree, list what changed recently, read a note or task in full, create and update notes, append to a note, read and create templates, instantiate a template, work with boards and tasks, link a note to a task, and read or add attachments.

Search and listings return ids, titles, and snippets rather than whole bodies, so an agent finds the right note before spending its context on it.

## What an agent cannot do

Nothing destructive. There is no delete, no trash, no purge, no restore, no moving notes around the tree, and no removing attachments or links. Those stay with you.

Everything an agent writes goes through the same history as your own edits, so you can read exactly what changed and put any note back the way it was.

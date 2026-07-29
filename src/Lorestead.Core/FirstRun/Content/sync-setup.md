Lorestead works fully offline and stays that way until you tell it otherwise. There is no account, no hosted tier, and nowhere for your notes to go that you did not set up yourself.

If you want the same notes on more than one device, you run the server.

## Run the server

You need a machine running Docker that your devices can reach - a home server, a NAS, a small VPS. The image is on Docker Hub and GHCR.

Make a directory for it, and in it a `compose.yaml`:

```yaml
services:
  lorestead:
    image: rthomasv3/lorestead-server
    ports:
      - "8080:8080"
    environment:
      LORESTEAD_TOKEN: pick-a-long-random-string
      LORESTEAD_DB_KEY: pick-another-one
    volumes:
      - ./data:/data
    restart: unless-stopped
```

Both variables are required - the server refuses to start without them. `LORESTEAD_TOKEN` is the bearer token every device and agent authenticates with. `LORESTEAD_DB_KEY` encrypts the server database at rest, so a copy of the volume is not a copy of your notes. `openssl rand -base64 32` makes a good value for each.

The container runs as uid 1654; on a Linux host, make the data directory writable by it: `chown -R 1654:1654 ./data`. Then start it and check it answers:

```
docker compose up -d
curl -H "Authorization: Bearer pick-a-long-random-string" http://localhost:8080/status
```

The second line returns the server version. A `401` means the token in the header does not match the one in the compose file - every endpoint needs it, so this check proves both the server and the token.

Two optional variables tune retention: `LORESTEAD_HISTORY_RETENTION` (versions kept per item, default 50) and `LORESTEAD_PURGE_RETENTION_DAYS` (days before deleted items purge for good, default 90).

The server's whole state is the `./data` directory. Back it up like any other directory - but it is only readable with the `LORESTEAD_DB_KEY` that wrote it, so keep your compose file safe too. To move the server to a new machine, copy both over and run `docker compose up -d` there.

## TLS

The server speaks plain HTTP. If any device reaches it from outside your own network, put a reverse proxy in front for TLS. With Caddy that is the whole config:

```
notes.example.com {
    reverse_proxy localhost:8080
}
```

Any proxy that terminates TLS works the same way; point it at port 8080.

## Claude from the web and your phone

Claude's web and mobile connectors cannot send a bearer token; they sign in with OAuth. Three more variables turn that on together:

```yaml
      LORESTEAD_OAUTH_CLIENT_ID: lorestead
      LORESTEAD_OAUTH_CLIENT_SECRET: generate-a-long-random-value
      LORESTEAD_PUBLIC_URL: https://notes.example.com
```

`LORESTEAD_PUBLIC_URL` is the address the outside world reaches your server at. claude.ai requires HTTPS, so for this path the reverse proxy is not optional. Then add a custom connector on claude.ai pointing at `https://notes.example.com/mcp` and enter the client id and secret in its advanced settings.

Rotating the secret signs every connector out on the next start. Redirect URIs default to Claude's own callbacks; `LORESTEAD_OAUTH_REDIRECT_URIS` overrides the list for a different host.

## Point the app at it

In Settings under Sync server, fill in the URL and paste the token. The status dot turns green once the app has talked to the server, and **Sync now** forces a pass whenever you want one.

Then repeat on your other devices, with the same URL and token.

## What sync does

Sync appends every edit to a log and applies it everywhere in order. If two devices change the same item while apart, the later edit wins - and the earlier one is still in that item's history, where you can read it and restore it.

Attachments move separately from the text, on demand, so a large file does not hold up the rest of a sync.

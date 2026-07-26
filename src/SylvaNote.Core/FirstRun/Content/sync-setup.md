SylvaNote works fully offline and stays that way until you tell it otherwise. There is no account, no hosted tier, and nowhere for your notes to go that you did not set up yourself.

If you want the same notes on more than one device, you run the server.

## Run the server

The server ships as a Docker image. A minimal `compose.yaml`:

```yaml
services:
  sylvanote:
    image: sylvanote-server
    ports:
      - "8080:8080"
    environment:
      SYLVANOTE_TOKEN: pick-a-long-random-string
      SYLVANOTE_DB_KEY: pick-another-one
    volumes:
      - ./data:/data
    restart: unless-stopped
```

Both variables are required - the server refuses to start without them. `SYLVANOTE_TOKEN` is the bearer token every device and agent authenticates with. `SYLVANOTE_DB_KEY` encrypts the server database at rest, so a copy of the volume is not a copy of your notes.

The server speaks plain HTTP. Put a reverse proxy in front of it for TLS if it is reachable from anywhere but your own network.

## Point the app at it

In Settings under Sync server, fill in the URL and paste the token. The status dot turns green once the app has talked to the server, and **Sync now** forces a pass whenever you want one.

Then repeat on your other devices, with the same URL and token.

## What sync does

Every edit is appended to a log and applied everywhere in order. If two devices change the same item while apart, the later edit wins - and the earlier one is still in that item's history, where you can read it and restore it.

Attachments move separately from the text, on demand, so a large file does not hold up the rest of a sync.

# Clipper share server

Zero-dependency Node.js server that hosts shared clips. Upload a clip → get a
`https://your-domain/c/<id>` link that plays in the browser and embeds inline in Discord.

## Endpoints

- `POST /api/upload` — body = raw MP4 bytes. Headers: `Authorization: Bearer <UPLOAD_TOKEN>`,
  `X-Title` (URL-encoded), optional `X-Width` / `X-Height`. Returns `{ id, url, video }`.
- `GET /c/:id` — HTML watch page with OpenGraph video tags (Discord inline player).
- `GET /v/:id.mp4` — the MP4, with HTTP range support (seeking).
- `DELETE /api/c/:id` — delete (needs the token).

## Run locally (test)

```bash
UPLOAD_TOKEN=testtoken PORT=8787 node server.js
```

## Deploy on the VPS

1. Install Node ≥ 18 (`node -v`).
2. Copy this `server/` folder to `/opt/clipper-share` on the VPS.
3. `cp .env.example .env` and edit: set a long random `UPLOAD_TOKEN`, your `PUBLIC_BASE_URL`
   (e.g. `https://clips.yourdomain.com`), and `DATA_DIR` (e.g. `/var/lib/clipper/clips`).
4. Create the data dir and a service user:
   ```bash
   sudo useradd -r -s /usr/sbin/nologin clipper || true
   sudo mkdir -p /var/lib/clipper/clips && sudo chown -R clipper /var/lib/clipper
   ```
5. Install the service: `sudo cp clipper-share.service /etc/systemd/system/ && sudo systemctl daemon-reload && sudo systemctl enable --now clipper-share`.
6. Put a TLS reverse proxy in front (Caddy is easiest):
   ```
   clips.yourdomain.com {
       reverse_proxy 127.0.0.1:8787
   }
   ```
   (or nginx with certbot). HTTPS is required for Discord to embed the video.
7. In the Clipper app → Settings, set **Share endpoint** = `https://clips.yourdomain.com`
   and **Share token** = the same `UPLOAD_TOKEN`.

## Notes

- Clips are stored as `<id>.mp4` + `<id>.json` (metadata) under `DATA_DIR`.
- `MAX_UPLOAD_MB` caps upload size (default 800).

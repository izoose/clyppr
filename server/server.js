'use strict';
// Clipper share server — zero-dependency Node.js. Stores uploaded clips and serves a
// watch page (with OpenGraph tags so Discord shows an inline player) + the raw MP4 with
// HTTP range support.
//
// Env:
//   PORT             (default 8787)
//   UPLOAD_TOKEN     shared secret required to upload (REQUIRED)
//   PUBLIC_BASE_URL  e.g. https://clips.example.com  (used to build share links)
//   DATA_DIR         where clips are stored (default ./data)
//   MAX_UPLOAD_MB    reject larger uploads (default 800)

const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PORT = parseInt(process.env.PORT || '8787', 10);
const TOKEN = process.env.UPLOAD_TOKEN || '';
const DATA_DIR = path.resolve(process.env.DATA_DIR || path.join(__dirname, 'data'));
const MAX_UPLOAD = (parseInt(process.env.MAX_UPLOAD_MB || '800', 10)) * 1024 * 1024;
const BASE_URL = (process.env.PUBLIC_BASE_URL || `http://localhost:${PORT}`).replace(/\/$/, '');

if (!TOKEN) { console.error('FATAL: set UPLOAD_TOKEN'); process.exit(1); }
fs.mkdirSync(DATA_DIR, { recursive: true });

const ID_RE = /^[A-Za-z0-9_-]{6,16}$/;
const newId = () => crypto.randomBytes(6).toString('base64url');
const clipPath = (id) => path.join(DATA_DIR, id + '.mp4');
const metaPath = (id) => path.join(DATA_DIR, id + '.json');
const esc = (s) => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

const server = http.createServer((req, res) => {
  const url = new URL(req.url, BASE_URL);
  const p = url.pathname;

  if (req.method === 'POST' && p === '/api/upload') return handleUpload(req, res);
  if (req.method === 'DELETE' && p.startsWith('/api/c/')) return handleDelete(req, res, p.slice('/api/c/'.length));
  if (req.method === 'GET' && p.startsWith('/c/')) return handleWatch(req, res, p.slice('/c/'.length));
  if (req.method === 'GET' && p.startsWith('/v/')) return handleVideo(req, res, p.slice('/v/'.length).replace(/\.mp4$/, ''));
  if (req.method === 'GET' && p === '/') { res.writeHead(200, { 'content-type': 'text/plain' }); return res.end('Clipper share server\n'); }

  res.writeHead(404, { 'content-type': 'text/plain' });
  res.end('Not found');
});

function handleUpload(req, res) {
  const auth = req.headers['authorization'] || '';
  if (auth !== 'Bearer ' + TOKEN) { res.writeHead(401); return res.end('Unauthorized'); }
  const len = parseInt(req.headers['content-length'] || '0', 10);
  if (len > MAX_UPLOAD) { res.writeHead(413); return res.end('Too large'); }

  const id = newId();
  const title = decodeURIComponent(req.headers['x-title'] || 'Clip');
  const width = parseInt(req.headers['x-width'] || '0', 10) || 0;
  const height = parseInt(req.headers['x-height'] || '0', 10) || 0;

  const out = fs.createWriteStream(clipPath(id));
  let bytes = 0, aborted = false;
  req.on('data', (chunk) => {
    bytes += chunk.length;
    if (bytes > MAX_UPLOAD && !aborted) { aborted = true; out.destroy(); req.destroy(); try { fs.unlinkSync(clipPath(id)); } catch {} res.writeHead(413); res.end('Too large'); }
  });
  req.pipe(out);
  out.on('error', () => { if (!res.headersSent) { res.writeHead(500); res.end('Write error'); } });
  out.on('finish', () => {
    if (aborted) return;
    fs.writeFileSync(metaPath(id), JSON.stringify({ title, width, height, size: bytes, createdAt: new Date().toISOString() }));
    const shareUrl = `${BASE_URL}/c/${id}`;
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ id, url: shareUrl, video: `${BASE_URL}/v/${id}.mp4` }));
  });
}

function readMeta(id) {
  try { return JSON.parse(fs.readFileSync(metaPath(id), 'utf8')); } catch { return null; }
}

function handleWatch(req, res, id) {
  if (!ID_RE.test(id) || !fs.existsSync(clipPath(id))) { res.writeHead(404); return res.end('Not found'); }
  const meta = readMeta(id) || { title: 'Clip', width: 1280, height: 720 };
  const videoUrl = `${BASE_URL}/v/${id}.mp4`;
  const pageUrl = `${BASE_URL}/c/${id}`;
  const html = `<!doctype html><html><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${esc(meta.title)} — Clipper</title>
<meta property="og:site_name" content="Clipper">
<meta property="og:title" content="${esc(meta.title)}">
<meta property="og:type" content="video.other">
<meta property="og:url" content="${pageUrl}">
<meta property="og:video" content="${videoUrl}">
<meta property="og:video:secure_url" content="${videoUrl}">
<meta property="og:video:type" content="video/mp4">
<meta property="og:video:width" content="${meta.width || 1280}">
<meta property="og:video:height" content="${meta.height || 720}">
<meta name="twitter:card" content="player">
<meta name="twitter:player:stream" content="${videoUrl}">
<meta name="twitter:player:stream:content_type" content="video/mp4">
<meta name="theme-color" content="#0e0e11">
<style>html,body{margin:0;height:100%;background:#0e0e11;color:#e9e9ec;font-family:Segoe UI,system-ui,sans-serif}
.wrap{min-height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;padding:20px;box-sizing:border-box}
video{max-width:min(1100px,100%);max-height:82vh;border-radius:12px;background:#000;box-shadow:0 8px 40px #0008}
.t{font-size:15px;color:#c9c9d2}.d{font-size:13px;color:#7a7a86;text-decoration:none}.d:hover{color:#e9e9ec}</style>
</head><body><div class="wrap">
<video src="${videoUrl}" controls autoplay playsinline></video>
<div class="t">${esc(meta.title)}</div>
<a class="d" href="${videoUrl}" download>Download</a>
</div></body></html>`;
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  res.end(html);
}

function handleVideo(req, res, id) {
  if (!ID_RE.test(id)) { res.writeHead(404); return res.end('Not found'); }
  const file = clipPath(id);
  let stat;
  try { stat = fs.statSync(file); } catch { res.writeHead(404); return res.end('Not found'); }

  const range = req.headers['range'];
  const headers = { 'content-type': 'video/mp4', 'accept-ranges': 'bytes', 'cache-control': 'public, max-age=31536000' };

  if (range) {
    const m = /bytes=(\d*)-(\d*)/.exec(range);
    let start = m && m[1] ? parseInt(m[1], 10) : 0;
    let end = m && m[2] ? parseInt(m[2], 10) : stat.size - 1;
    if (isNaN(start) || isNaN(end) || start > end || end >= stat.size) { start = 0; end = stat.size - 1; }
    res.writeHead(206, { ...headers, 'content-range': `bytes ${start}-${end}/${stat.size}`, 'content-length': end - start + 1 });
    fs.createReadStream(file, { start, end }).pipe(res);
  } else {
    res.writeHead(200, { ...headers, 'content-length': stat.size });
    fs.createReadStream(file).pipe(res);
  }
}

function handleDelete(req, res, id) {
  if ((req.headers['authorization'] || '') !== 'Bearer ' + TOKEN) { res.writeHead(401); return res.end('Unauthorized'); }
  if (!ID_RE.test(id)) { res.writeHead(400); return res.end('Bad id'); }
  try { fs.unlinkSync(clipPath(id)); } catch {}
  try { fs.unlinkSync(metaPath(id)); } catch {}
  res.writeHead(200); res.end('ok');
}

server.listen(PORT, () => console.log(`Clipper share server on :${PORT}  base=${BASE_URL}  data=${DATA_DIR}`));

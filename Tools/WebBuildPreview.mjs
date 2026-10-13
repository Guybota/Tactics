import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
const directory = fs.realpathSync(process.argv[2]);
const port = Number(process.argv[3] || 8129);
const types = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css',
  '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon' };
http.createServer((request, response) => {
  let relative;
  try { relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname); }
  catch { response.writeHead(400).end(); return; }
  if (relative === '/') relative = '/index.html';
  const file = path.resolve(directory, '.' + relative);
  if (!file.startsWith(directory + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    response.writeHead(404).end(); return;
  }
  const compressed = file.endsWith('.br') ? 'br' : file.endsWith('.gz') ? 'gzip' : null;
  const extension = path.extname(compressed ? file.slice(0, -3) : file);
  response.setHeader('Content-Type', types[extension] || 'application/octet-stream');
  response.setHeader('Cache-Control', 'no-store');
  if (compressed) response.setHeader('Content-Encoding', compressed);
  fs.createReadStream(file).pipe(response);
}).listen(port, '127.0.0.1', () => process.stdout.write(`Web build preview: http://127.0.0.1:${port}\n`));

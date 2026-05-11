"""
ModLauncher Mod Server
Раздаёт файлы модов и предоставляет API для проверки версий.

Использование:
  1. Положите .pak файл мода в папку ./mods/
  2. Создайте/отредактируйте mod_info.json (генерируется автоматически при первом запуске)
  3. Запустите: python server.py [--port PORT] [--host HOST]

API:
  GET /api/version        — текущая версия мода (JSON)
  GET /api/download       — скачать .pak файл мода
  GET /api/health         — проверка работоспособности
"""

import hashlib
import json
import os
import sys
from http.server import HTTPServer, SimpleHTTPRequestHandler
from urllib.parse import urlparse

CONFIG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mod_info.json")
MODS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mods")


def load_config():
    if not os.path.exists(CONFIG_PATH):
        default = {
            "mod_name": "Chebovka",
            "version": "1.5.2",
            "file_name": "Chebovka1.5.2.pak",
            "changelog": "Initial release"
        }
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump(default, f, indent=2, ensure_ascii=False)
        return default

    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def compute_sha256(filepath):
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()


class ModServerHandler(SimpleHTTPRequestHandler):
    def do_GET(self):
        parsed = urlparse(self.path)
        path = parsed.path.rstrip("/")

        if path == "/api/version":
            self.handle_version()
        elif path == "/api/download":
            self.handle_download()
        elif path == "/api/health":
            self.handle_health()
        else:
            self.send_error(404, "Not Found")

    def handle_version(self):
        config = load_config()
        mod_path = os.path.join(MODS_DIR, config["file_name"])

        response = {
            "mod_name": config["mod_name"],
            "version": config["version"],
            "file_name": config["file_name"],
            "changelog": config.get("changelog", ""),
            "available": os.path.exists(mod_path),
        }

        if os.path.exists(mod_path):
            response["sha256"] = compute_sha256(mod_path)
            response["file_size"] = os.path.getsize(mod_path)

        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(json.dumps(response, ensure_ascii=False, indent=2).encode("utf-8"))

    def handle_download(self):
        config = load_config()
        mod_path = os.path.join(MODS_DIR, config["file_name"])

        if not os.path.exists(mod_path):
            self.send_error(404, "Mod file not found on server")
            return

        file_size = os.path.getsize(mod_path)

        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Disposition", f'attachment; filename="{config["file_name"]}"')
        self.send_header("Content-Length", str(file_size))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()

        with open(mod_path, "rb") as f:
            while True:
                chunk = f.read(65536)
                if not chunk:
                    break
                self.wfile.write(chunk)

    def handle_health(self):
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b'{"status":"ok"}')

    def log_message(self, format, *args):
        print(f"[ModServer] {args[0]}")


def main():
    host = "0.0.0.0"
    port = 8080

    for i, arg in enumerate(sys.argv[1:], 1):
        if arg == "--port" and i < len(sys.argv) - 1:
            port = int(sys.argv[i + 1])
        elif arg == "--host" and i < len(sys.argv) - 1:
            host = sys.argv[i + 1]

    os.makedirs(MODS_DIR, exist_ok=True)

    config = load_config()
    mod_path = os.path.join(MODS_DIR, config["file_name"])

    print(f"[ModServer] Starting on {host}:{port}")
    print(f"[ModServer] Mod: {config['mod_name']} v{config['version']}")
    if os.path.exists(mod_path):
        size_mb = os.path.getsize(mod_path) / (1024 * 1024)
        print(f"[ModServer] File: {config['file_name']} ({size_mb:.1f} MB)")
    else:
        print(f"[ModServer] WARNING: {config['file_name']} not found in {MODS_DIR}/")
        print(f"[ModServer] Place the mod file there to enable downloads.")

    server = HTTPServer((host, port), ModServerHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[ModServer] Shutting down.")
        server.server_close()


if __name__ == "__main__":
    main()

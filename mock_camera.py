import socket
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler
import base64

# A minimal 1x1 pixel JPEG image
TINY_JPEG = base64.b64decode(
    "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA="
)

def rtsp_server():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(('127.0.0.1', 8554))
    server.listen(5)
    print("[MOCK] RTSP Simulator bound to 127.0.0.1:8554")
    while True:
        try:
            client, addr = server.accept()
            data = client.recv(1024)
            if b"OPTIONS" in data:
                print(f"[MOCK] Received RTSP OPTIONS from {addr}")
                # RTSP/1.0 200 OK is enough to trigger RtspScanner CheckPathAsync logic
                response = b"RTSP/1.0 200 OK\r\nCSeq: 1\r\nPublic: OPTIONS, DESCRIBE, SETUP, TEARDOWN, PLAY, PAUSE\r\n\r\n"
                client.sendall(response)
            client.close()
        except:
            pass

class SnapshotHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        print(f"[MOCK] Received HTTP GET {self.path}")
        self.send_response(200)
        self.send_header("Content-type", "image/jpeg")
        self.end_headers()
        self.wfile.write(TINY_JPEG)
        
    def log_message(self, format, *args):
        pass # Suppress verbose logs

def http_server():
    server = HTTPServer(('127.0.0.1', 8080), SnapshotHandler)
    print("[MOCK] HTTP Snapshot API bound to 127.0.0.1:8080")
    server.serve_forever()

if __name__ == '__main__':
    t1 = threading.Thread(target=rtsp_server, daemon=True)
    t2 = threading.Thread(target=http_server, daemon=True)
    t1.start()
    t2.start()
    
    print("[MOCK] Network Probe Simulator Active. Press Ctrl+C to exit.")
    try:
        while True:
            import time
            time.sleep(1)
    except KeyboardInterrupt:
        print("[MOCK] Shutting down.")

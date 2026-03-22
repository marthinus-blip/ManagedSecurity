#!/bin/bash
# Natively automated virtual hardware verification environment.
# Completely bypasses the missing docker-compose dependency elegantly.

echo "[SIMULATION] Booting Zero-Trust RTSP Spine (mediamtx) on Port 8555..."
docker run --rm -d --name rtsp-server -p 8555:8554 bluenviron/mediamtx:latest
sleep 2

echo "[SIMULATION] Injecting Ground Truth (Positive) Feed..."
docker run --rm -d --name positive-stream --network container:rtsp-server \
    -v "$(pwd)/pedestrian.avi":/positive.avi:ro \
    linuxserver/ffmpeg:latest \
    -re -stream_loop -1 -i /positive.avi -c:v libx264 -preset ultrafast -b:v 1000k -f rtsp rtsp://localhost:8554/positive

echo "[SIMULATION] Injecting Negative Silence Topology..."
docker run --rm -d --name negative-stream --network container:rtsp-server \
    linuxserver/ffmpeg:latest \
    -re -f lavfi -i color=c=black:s=640x640:r=15 -c:v libx264 -preset ultrafast -b:v 500k -f rtsp rtsp://localhost:8554/negative

echo ""
echo "[SIMULATION] Hardware Boundary Synthesized Successfully!"
echo "--------------------------------------------------------"
echo "To test POSITIVE Ground Truth natively:"
echo "cd ManagedSecurity.Sentinel && dotnet run -- onvif-diag positive"
echo ""
echo "To test NEGATIVE Silence natively:"
echo "cd ManagedSecurity.Sentinel && dotnet run -- onvif-diag negative"
echo "--------------------------------------------------------"
echo "To teardown dynamically: docker stop rtsp-server positive-stream negative-stream"

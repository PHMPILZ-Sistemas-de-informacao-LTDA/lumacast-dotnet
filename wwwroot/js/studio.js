(() => {
    "use strict";

    const byId = (id) => document.getElementById(id);
    const elements = {
        video: byId("cameraPreview"),
        stage: byId("stage"),
        gate: byId("cameraGate"),
        gateTitle: byId("gateTitle"),
        gateDescription: byId("gateDescription"),
        activate: byId("activateCamera"),
        error: byId("cameraError"),
        controls: byId("stageControls"),
        cameraOff: byId("cameraOffState"),
        liveBadge: byId("liveBadge"),
        timer: byId("liveTimer"),
        toggleMic: byId("toggleMic"),
        toggleCamera: byId("toggleCamera"),
        switchCamera: byId("switchCamera"),
        pip: byId("pictureInPicture"),
        fullscreen: byId("fullscreen"),
        cameraSelect: byId("cameraSelect"),
        microphoneSelect: byId("microphoneSelect"),
        quality: byId("quality"),
        title: byId("streamTitle"),
        titleCount: byId("titleCount"),
        cameraCheck: byId("cameraCheck"),
        audioCheck: byId("audioCheck"),
        readiness: byId("readinessDot"),
        providerStatus: byId("providerStatus"),
        start: byId("startBroadcast"),
        end: byId("endBroadcast"),
        viewerStatus: byId("viewerStatus"),
        viewerCount: byId("viewerCount"),
        copyLink: byId("copyLink"),
        download: byId("downloadRecording"),
        recordingNote: byId("recordingNote")
    };

    const state = {
        stream: null,
        socket: null,
        recorder: null,
        chunks: [],
        recordingUrl: "",
        roomId: "",
        viewerUrl: "",
        provider: "peer-to-peer",
        liveKitRoom: null,
        liveKitPublications: { video: null, audio: null },
        broadcastKey: "",
        peers: new Map(),
        live: false,
        elapsed: 0,
        timerId: null,
        micEnabled: true,
        cameraEnabled: true,
        facingMode: "user"
    };

    const rtcConfiguration = {
        iceServers: [{ urls: "stun:stun.l.google.com:19302" }]
    };

    async function refreshProviderStatus() {
        try {
            const response = await fetch("/api/livekit/status", { headers: { Accept: "application/json" } });
            const status = await response.json();
            state.provider = status.configured ? "livekit" : "peer-to-peer";
            elements.providerStatus.classList.toggle("livekit", status.configured);
            elements.providerStatus.lastChild.textContent = status.configured
                ? " LiveKit SFU pronto para transmitir"
                : " Modo local P2P — configure o LiveKit para escalar";
        } catch {
            state.provider = "peer-to-peer";
            elements.providerStatus.lastChild.textContent = " Modo local P2P disponível";
        }
    }

    function qualityConstraints() {
        const selected = elements.quality.value;
        if (selected === "480") return { width: { ideal: 854 }, height: { ideal: 480 }, frameRate: { ideal: 30 } };
        if (selected === "720") return { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } };
        return { width: { ideal: 1920 }, height: { ideal: 1080 }, frameRate: { ideal: 30 } };
    }

    function mediaConstraints(cameraId, microphoneId, facingMode) {
        return {
            video: {
                ...qualityConstraints(),
                ...(cameraId ? { deviceId: { exact: cameraId } } : { facingMode: { ideal: facingMode } })
            },
            audio: microphoneId
                ? { deviceId: { exact: microphoneId }, echoCancellation: true, noiseSuppression: true }
                : { echoCancellation: true, noiseSuppression: true }
        };
    }

    async function loadDevices() {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const cameras = devices.filter((device) => device.kind === "videoinput");
        const microphones = devices.filter((device) => device.kind === "audioinput");
        const currentCamera = elements.cameraSelect.value;
        const currentMicrophone = elements.microphoneSelect.value;

        elements.cameraSelect.replaceChildren(...cameras.map((device, index) => optionFor(device, `Câmera ${index + 1}`)));
        elements.microphoneSelect.replaceChildren(...microphones.map((device, index) => optionFor(device, `Microfone ${index + 1}`)));

        if (cameras.some((device) => device.deviceId === currentCamera)) elements.cameraSelect.value = currentCamera;
        if (microphones.some((device) => device.deviceId === currentMicrophone)) elements.microphoneSelect.value = currentMicrophone;
        elements.cameraSelect.disabled = state.live;
        elements.microphoneSelect.disabled = state.live;
    }

    function optionFor(device, fallback) {
        const option = document.createElement("option");
        option.value = device.deviceId;
        option.textContent = device.label || fallback;
        return option;
    }

    function stopStream() {
        state.stream?.getTracks().forEach((track) => track.stop());
        state.stream = null;
        elements.video.srcObject = null;
    }

    async function startCamera(options = {}) {
        if (!navigator.mediaDevices?.getUserMedia) {
            showCameraError("Este navegador não oferece acesso à câmera. Use uma versão atualizada do Chrome, Safari ou Edge.");
            return null;
        }

        elements.activate.disabled = true;
        elements.gateTitle.textContent = "Preparando sua câmera…";
        elements.gateDescription.textContent = "Autorize o vídeo e o áudio na janela do navegador.";
        elements.error.hidden = true;

        try {
            stopStream();
            const stream = await navigator.mediaDevices.getUserMedia(mediaConstraints(
                options.cameraId ?? elements.cameraSelect.value,
                options.microphoneId ?? elements.microphoneSelect.value,
                options.facingMode ?? state.facingMode
            ));

            state.stream = stream;
            stream.getAudioTracks().forEach((track) => { track.enabled = state.micEnabled; });
            stream.getVideoTracks().forEach((track) => { track.enabled = state.cameraEnabled; });
            elements.video.srcObject = stream;
            await elements.video.play();
            await loadDevices();
            setCameraReady(true);
            return stream;
        } catch (error) {
            const blocked = error instanceof DOMException && error.name === "NotAllowedError";
            showCameraError(blocked
                ? "O acesso foi bloqueado. Libere câmera e microfone nas configurações do navegador e tente novamente."
                : "Não foi possível iniciar a câmera. Verifique se ela não está em uso por outro aplicativo.");
            return null;
        } finally {
            elements.activate.disabled = false;
        }
    }

    function setCameraReady(ready) {
        elements.gate.hidden = ready;
        elements.controls.classList.toggle("visible", ready);
        elements.stage.classList.toggle("stage-ready", ready);
        elements.readiness.classList.toggle("ready", ready);
        elements.readiness.setAttribute("aria-label", ready ? "Pronto" : "Aguardando câmera");
        elements.cameraCheck.textContent = ready ? "✓ Câmera" : "○ Câmera";
        elements.audioCheck.textContent = ready && state.micEnabled ? "✓ Áudio" : "○ Áudio";
        elements.cameraCheck.classList.toggle("checked", ready);
        elements.audioCheck.classList.toggle("checked", ready && state.micEnabled);
    }

    function showCameraError(message) {
        setCameraReady(false);
        elements.gateTitle.textContent = "Não conseguimos acessar sua câmera";
        elements.gateDescription.textContent = "Revise a permissão do navegador e tente novamente.";
        elements.error.textContent = message;
        elements.error.hidden = false;
    }

    async function toggleMicrophone() {
        state.micEnabled = !state.micEnabled;
        state.stream?.getAudioTracks().forEach((track) => { track.enabled = state.micEnabled; });
        const publication = state.liveKitPublications.audio;
        if (publication) await (state.micEnabled ? publication.unmute() : publication.mute());
        elements.toggleMic.classList.toggle("is-off", !state.micEnabled);
        elements.toggleMic.querySelector("span").textContent = state.micEnabled ? "◉" : "×";
        elements.toggleMic.querySelector("b").textContent = state.micEnabled ? "Microfone" : "Sem áudio";
        elements.toggleMic.setAttribute("aria-label", state.micEnabled ? "Desativar microfone" : "Ativar microfone");
        elements.audioCheck.textContent = state.micEnabled ? "✓ Áudio" : "○ Áudio";
        elements.audioCheck.classList.toggle("checked", state.micEnabled);
    }

    async function toggleCamera() {
        state.cameraEnabled = !state.cameraEnabled;
        state.stream?.getVideoTracks().forEach((track) => { track.enabled = state.cameraEnabled; });
        const publication = state.liveKitPublications.video;
        if (publication) await (state.cameraEnabled ? publication.unmute() : publication.mute());
        elements.toggleCamera.classList.toggle("is-off", !state.cameraEnabled);
        elements.toggleCamera.querySelector("span").textContent = state.cameraEnabled ? "▣" : "×";
        elements.toggleCamera.querySelector("b").textContent = state.cameraEnabled ? "Câmera" : "Sem vídeo";
        elements.toggleCamera.setAttribute("aria-label", state.cameraEnabled ? "Desativar câmera" : "Ativar câmera");
        elements.cameraOff.hidden = state.cameraEnabled;
    }

    async function switchCamera() {
        if (state.live) return;
        const options = [...elements.cameraSelect.options];
        if (options.length > 1) {
            const current = options.findIndex((option) => option.value === elements.cameraSelect.value);
            const next = options[(current + 1) % options.length];
            elements.cameraSelect.value = next.value;
            await startCamera({ cameraId: next.value });
            return;
        }

        state.facingMode = state.facingMode === "user" ? "environment" : "user";
        await startCamera({ cameraId: "", facingMode: state.facingMode });
    }

    function makeRoomId() {
        const bytes = new Uint8Array(5);
        crypto.getRandomValues(bytes);
        return [...bytes].map((value) => value.toString(36).padStart(2, "0")).join("").slice(0, 8);
    }

    function socketUrl(roomId, role, clientId) {
        const protocol = location.protocol === "https:" ? "wss:" : "ws:";
        const query = new URLSearchParams({ room: roomId, role, id: clientId });
        return `${protocol}//${location.host}/signal?${query}`;
    }

    function sendSignal(payload) {
        if (state.socket?.readyState === WebSocket.OPEN) state.socket.send(JSON.stringify(payload));
    }

    async function fetchJson(url, options = {}) {
        const response = await fetch(url, {
            ...options,
            headers: { "Content-Type": "application/json", Accept: "application/json", ...options.headers }
        });
        if (!response.ok) {
            let message = "Não foi possível conectar ao serviço de transmissão.";
            try {
                const body = await response.json();
                message = body.detail || body.message || message;
            } catch { }
            throw new Error(message);
        }
        return response.status === 204 ? null : response.json();
    }

    async function connectLiveKitBroadcaster() {
        if (!window.LivekitClient) throw new Error("O cliente LiveKit não pôde ser carregado.");

        const registration = await fetchJson("/api/livekit/rooms", { method: "POST", body: "{}" });
        state.roomId = registration.roomName;
        state.broadcastKey = registration.broadcastKey;
        const credential = await fetchJson("/api/livekit/token", {
            method: "POST",
            body: JSON.stringify({
                roomName: state.roomId,
                role: "broadcaster",
                participantName: "Apresentador",
                broadcastKey: state.broadcastKey
            })
        });

        const { Room, RoomEvent, Track } = window.LivekitClient;
        const room = new Room({ adaptiveStream: true, dynacast: true });
        state.liveKitRoom = room;
        const updateViewerCount = () => {
            elements.viewerCount.textContent = String(room.remoteParticipants.size);
        };
        room.on(RoomEvent.ParticipantConnected, updateViewerCount);
        room.on(RoomEvent.ParticipantDisconnected, updateViewerCount);
        room.on(RoomEvent.Disconnected, () => {
            if (state.live) endBroadcast(false);
        });

        await room.connect(credential.server_url, credential.participant_token);
        const videoTrack = state.stream.getVideoTracks()[0]?.clone();
        const audioTrack = state.stream.getAudioTracks()[0]?.clone();
        if (videoTrack) {
            state.liveKitPublications.video = await room.localParticipant.publishTrack(videoTrack, {
                source: Track.Source.Camera,
                simulcast: true
            });
        }
        if (audioTrack) {
            state.liveKitPublications.audio = await room.localParticipant.publishTrack(audioTrack, {
                source: Track.Source.Microphone
            });
        }
        updateViewerCount();
    }

    async function connectBroadcaster(roomId) {
        return new Promise((resolve, reject) => {
            const socket = new WebSocket(socketUrl(roomId, "broadcaster", "host"));
            state.socket = socket;
            socket.onopen = resolve;
            socket.onerror = () => reject(new Error("Não foi possível abrir a sala de transmissão."));
            socket.onmessage = async (event) => {
                const message = JSON.parse(event.data);
                if (message.type === "viewer-joined") await createPeerForViewer(message.viewerId);
                if (message.type === "answer") await state.peers.get(message.viewerId)?.setRemoteDescription(message.sdp);
                if (message.type === "ice-candidate" && message.candidate) {
                    await state.peers.get(message.viewerId)?.addIceCandidate(message.candidate);
                }
                if (message.type === "viewer-left") closeViewerPeer(message.viewerId);
                if (message.type === "viewer-count") elements.viewerCount.textContent = String(message.count);
            };
            socket.onclose = () => {
                if (state.live) endBroadcast(false);
            };
        });
    }

    async function createPeerForViewer(viewerId) {
        closeViewerPeer(viewerId);
        const peer = new RTCPeerConnection(rtcConfiguration);
        state.peers.set(viewerId, peer);
        state.stream.getTracks().forEach((track) => peer.addTrack(track, state.stream));
        peer.onicecandidate = (event) => {
            if (event.candidate) sendSignal({ type: "ice-candidate", viewerId, candidate: event.candidate });
        };
        peer.onconnectionstatechange = () => {
            if (["failed", "closed"].includes(peer.connectionState)) closeViewerPeer(viewerId);
        };
        const offer = await peer.createOffer();
        await peer.setLocalDescription(offer);
        sendSignal({ type: "offer", viewerId, sdp: peer.localDescription });
    }

    function closeViewerPeer(viewerId) {
        state.peers.get(viewerId)?.close();
        state.peers.delete(viewerId);
    }

    function startRecorder() {
        if (typeof MediaRecorder === "undefined") {
            elements.recordingNote.textContent = "A gravação local não é compatível com este navegador.";
            return;
        }

        if (state.recordingUrl) URL.revokeObjectURL(state.recordingUrl);
        state.chunks = [];
        const types = ["video/webm;codecs=vp9,opus", "video/webm;codecs=vp8,opus", "video/webm", "video/mp4"];
        const mimeType = types.find((type) => MediaRecorder.isTypeSupported(type));
        state.recorder = mimeType ? new MediaRecorder(state.stream, { mimeType }) : new MediaRecorder(state.stream);
        state.recorder.ondataavailable = (event) => { if (event.data.size) state.chunks.push(event.data); };
        state.recorder.onstop = () => {
            const type = state.recorder.mimeType || "video/webm";
            const extension = type.includes("mp4") ? "mp4" : "webm";
            state.recordingUrl = URL.createObjectURL(new Blob(state.chunks, { type }));
            elements.download.href = state.recordingUrl;
            elements.download.download = `lumacast-${state.roomId}.${extension}`;
            elements.download.hidden = false;
            elements.recordingNote.hidden = true;
        };
        state.recorder.start(1000);
    }

    async function startBroadcast() {
        elements.start.disabled = true;
        try {
            if (!state.stream) await startCamera();
            if (!state.stream) return;

            await refreshProviderStatus();
            if (state.provider === "livekit") {
                await connectLiveKitBroadcaster();
            } else {
                state.roomId = makeRoomId();
                await connectBroadcaster(state.roomId);
            }
            const watchUrl = new URL("/Assistir", location.origin);
            watchUrl.searchParams.set("room", state.roomId);
            watchUrl.searchParams.set("title", elements.title.value.trim() || "Transmissão LumaCast");
            watchUrl.searchParams.set("provider", state.provider);
            state.viewerUrl = watchUrl.toString();

            state.live = true;
            state.elapsed = 0;
            state.timerId = window.setInterval(() => {
                state.elapsed += 1;
                elements.timer.textContent = formatTime(state.elapsed);
            }, 1000);
            startRecorder();
            elements.liveBadge.hidden = false;
            elements.start.hidden = true;
            elements.end.hidden = false;
            elements.viewerStatus.hidden = false;
            elements.cameraSelect.disabled = true;
            elements.microphoneSelect.disabled = true;
            elements.quality.disabled = true;
            elements.switchCamera.disabled = true;
        } catch (error) {
            showCameraError(error.message || "Não foi possível iniciar a transmissão.");
        } finally {
            elements.start.disabled = false;
        }
    }

    async function endBroadcast(closeTransport = true) {
        state.live = false;
        if (state.timerId) window.clearInterval(state.timerId);
        state.timerId = null;
        if (state.recorder?.state === "recording") state.recorder.stop();
        state.peers.forEach((peer) => peer.close());
        state.peers.clear();
        if (closeTransport) state.socket?.close(1000, "Transmissão encerrada");
        state.socket = null;
        if (state.liveKitRoom) {
            state.liveKitRoom.disconnect();
            state.liveKitRoom = null;
        }
        state.liveKitPublications = { video: null, audio: null };
        if (state.provider === "livekit" && state.broadcastKey) {
            fetch(`/api/livekit/rooms/${encodeURIComponent(state.roomId)}/end`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ broadcastKey: state.broadcastKey }),
                keepalive: true
            }).catch(() => {});
        }
        state.broadcastKey = "";
        elements.liveBadge.hidden = true;
        elements.start.hidden = false;
        elements.end.hidden = true;
        elements.viewerStatus.hidden = true;
        elements.cameraSelect.disabled = false;
        elements.microphoneSelect.disabled = false;
        elements.quality.disabled = false;
        elements.switchCamera.disabled = false;
        elements.viewerCount.textContent = "0";
    }

    function formatTime(totalSeconds) {
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;
        return [hours, minutes, seconds].map((value) => String(value).padStart(2, "0")).join(":");
    }

    async function copyViewerLink() {
        try {
            await navigator.clipboard.writeText(state.viewerUrl);
            elements.copyLink.textContent = "Link copiado ✓";
        } catch {
            window.prompt("Copie o link da transmissão:", state.viewerUrl);
        }
        window.setTimeout(() => { elements.copyLink.textContent = "Copiar link"; }, 2200);
    }

    elements.activate.addEventListener("click", () => startCamera());
    elements.toggleMic.addEventListener("click", toggleMicrophone);
    elements.toggleCamera.addEventListener("click", toggleCamera);
    elements.switchCamera.addEventListener("click", switchCamera);
    elements.start.addEventListener("click", startBroadcast);
    elements.end.addEventListener("click", () => endBroadcast());
    elements.copyLink.addEventListener("click", copyViewerLink);
    elements.title.addEventListener("input", () => { elements.titleCount.textContent = String(elements.title.value.length); });
    elements.cameraSelect.addEventListener("change", () => startCamera({ cameraId: elements.cameraSelect.value }));
    elements.microphoneSelect.addEventListener("change", () => startCamera({ microphoneId: elements.microphoneSelect.value }));
    elements.quality.addEventListener("change", () => { if (state.stream && !state.live) startCamera(); });
    elements.pip.addEventListener("click", async () => {
        if (document.pictureInPictureEnabled && elements.video.srcObject) await elements.video.requestPictureInPicture();
    });
    elements.fullscreen.addEventListener("click", () => elements.stage.requestFullscreen());
    window.addEventListener("beforeunload", () => {
        state.socket?.close();
        state.liveKitRoom?.disconnect();
        state.stream?.getTracks().forEach((track) => track.stop());
        if (state.recordingUrl) URL.revokeObjectURL(state.recordingUrl);
    });
    refreshProviderStatus();
})();

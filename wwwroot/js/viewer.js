(() => {
    "use strict";

    const video = document.getElementById("remoteVideo");
    const placeholder = document.getElementById("viewerPlaceholder");
    const message = document.getElementById("viewerMessage");
    const description = document.getElementById("viewerDescription");
    const liveLabel = document.getElementById("viewerLiveLabel");
    const title = document.getElementById("viewerTitle");
    const backLink = document.getElementById("backToStudio");
    const parameters = new URLSearchParams(location.search);
    const roomId = parameters.get("room");
    const provider = parameters.get("provider") || "peer-to-peer";
    const viewerId = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`;
    let socket;
    let peer;
    let liveKitRoom;
    let receivedMedia = false;

    title.textContent = parameters.get("title") || "Transmissão LumaCast";

    const rtcConfiguration = {
        iceServers: [{ urls: "stun:stun.l.google.com:19302" }]
    };

    function socketUrl() {
        const protocol = location.protocol === "https:" ? "wss:" : "ws:";
        const query = new URLSearchParams({ room: roomId, role: "viewer", id: viewerId });
        return `${protocol}//${location.host}/signal?${query}`;
    }

    function send(payload) {
        if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify(payload));
    }

    function createPeer() {
        peer?.close();
        peer = new RTCPeerConnection(rtcConfiguration);
        peer.onicecandidate = (event) => {
            if (event.candidate) send({ type: "ice-candidate", candidate: event.candidate });
        };
        peer.ontrack = (event) => {
            receivedMedia = true;
            video.srcObject = event.streams[0];
            video.play().catch(() => {});
            placeholder.hidden = true;
            liveLabel.innerHTML = "<i></i> AO VIVO";
            liveLabel.classList.add("is-live");
        };
        peer.onconnectionstatechange = () => {
            if (["failed", "disconnected", "closed"].includes(peer.connectionState)) showOffline();
        };
        return peer;
    }

    async function connectLiveKitViewer() {
        if (!window.LivekitClient) throw new Error("Cliente LiveKit indisponível.");
        const response = await fetch("/api/livekit/token", {
            method: "POST",
            headers: { "Content-Type": "application/json", Accept: "application/json" },
            body: JSON.stringify({
                roomName: roomId,
                role: "viewer",
                participantName: "Espectador"
            })
        });
        if (!response.ok) throw new Error("Transmissão indisponível.");
        const credential = await response.json();
        const { Room, RoomEvent, Track } = window.LivekitClient;
        liveKitRoom = new Room({ adaptiveStream: true, dynacast: true });
        liveKitRoom.on(RoomEvent.TrackSubscribed, (track) => {
            if (track.kind !== Track.Kind.Video && track.kind !== Track.Kind.Audio) return;
            track.attach(video);
            receivedMedia = true;
            placeholder.hidden = true;
            liveLabel.innerHTML = "<i></i> AO VIVO";
            liveLabel.classList.add("is-live");
            video.play().catch(() => {});
        });
        liveKitRoom.on(RoomEvent.TrackUnsubscribed, (track) => track.detach(video));
        liveKitRoom.on(RoomEvent.Disconnected, showOffline);
        await liveKitRoom.connect(credential.server_url, credential.participant_token);
    }

    async function handleSignal(event) {
        const payload = JSON.parse(event.data);
        if (payload.type === "offline") {
            showOffline();
            return;
        }
        if (payload.type === "full") {
            showOffline(
                "A sala atingiu o limite do modo local",
                "Peça ao anfitrião para ativar o LiveKit e tente novamente."
            );
            return;
        }
        if (payload.type === "offer") {
            if (!peer) createPeer();
            await peer.setRemoteDescription(payload.sdp);
            const answer = await peer.createAnswer();
            await peer.setLocalDescription(answer);
            send({ type: "answer", sdp: peer.localDescription });
        }
        if (payload.type === "ice-candidate" && payload.candidate) {
            if (!peer) createPeer();
            await peer.addIceCandidate(payload.candidate);
        }
    }

    function showOffline(customTitle, customDescription) {
        const hasCustomMessage = typeof customTitle === "string";
        placeholder.hidden = false;
        message.textContent = hasCustomMessage
            ? customTitle
            : (receivedMedia ? "A transmissão foi encerrada" : "Transmissão indisponível");
        description.textContent = hasCustomMessage
            ? customDescription
            : (receivedMedia
                ? "Obrigado por acompanhar. Você já pode fechar esta página."
                : "O anfitrião ainda não entrou no ar ou o link não está mais ativo.");
        backLink.hidden = false;
        liveLabel.innerHTML = "<i></i> OFFLINE";
        liveLabel.classList.remove("is-live");
        video.srcObject = null;
    }

    if (!roomId) {
        showOffline();
    } else if (provider === "livekit") {
        connectLiveKitViewer().catch(showOffline);
    } else {
        socket = new WebSocket(socketUrl());
        socket.onmessage = handleSignal;
        socket.onerror = showOffline;
        socket.onclose = () => { if (!receivedMedia || peer?.connectionState !== "connected") showOffline(); };
    }

    document.getElementById("viewerFullscreen").addEventListener("click", () => {
        document.querySelector(".viewer-video-shell").requestFullscreen();
    });
    window.addEventListener("beforeunload", () => {
        socket?.close();
        peer?.close();
        liveKitRoom?.disconnect();
    });
})();

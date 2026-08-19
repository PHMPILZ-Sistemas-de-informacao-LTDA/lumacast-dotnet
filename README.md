# LumaCast

Estúdio de transmissão ao vivo criado com **ASP.NET Core 8**, Razor Pages, WebRTC e LiveKit.

O projeto funciona em dois modos:

- **LiveKit SFU (recomendado):** sinalização, TURN e distribuição escalável para vários espectadores;
- **P2P local:** fallback automático para desenvolvimento quando o LiveKit ainda não está configurado.

## Recursos

- acesso à câmera e ao microfone do dispositivo;
- pré-visualização antes de entrar no ar;
- seleção de câmera, microfone e qualidade;
- sala ao vivo com link compartilhável;
- página exclusiva para espectadores;
- contagem de espectadores conectados;
- gravação local com download ao encerrar;
- tokens separados para apresentador e espectadores;
- chave secreta do LiveKit mantida somente no backend.

## Executar em modo local

```bash
dotnet restore
dotnet run --launch-profile https
```

Abra o endereço HTTPS exibido no terminal. O navegador exige HTTPS para acessar câmera e microfone fora de `localhost`.

Sem credenciais, o estúdio usa automaticamente a sinalização WebSocket interna e conexões WebRTC ponto a ponto.

## Configurar o LiveKit Cloud

1. Crie um projeto no [LiveKit Cloud](https://cloud.livekit.io/).
2. Copie a URL WebSocket, a API key e o API secret do projeto.
3. Defina as variáveis no ambiente antes de iniciar a aplicação:

```bash
export LIVEKIT_URL="wss://seu-projeto.livekit.cloud"
export LIVEKIT_API_KEY="sua-api-key"
export LIVEKIT_API_SECRET="seu-api-secret"
dotnet run --launch-profile https
```

As credenciais também podem ser fornecidas pelas chaves `LiveKit:Url`, `LiveKit:ApiKey` e `LiveKit:ApiSecret` da configuração do ASP.NET Core. Não salve segredos no repositório.

## Segurança das salas

O backend cria uma chave aleatória exclusiva para o apresentador. Tokens de espectadores possuem somente permissão para entrar e assistir; eles não podem publicar áudio, vídeo ou dados. As salas expiram no registro local após 12 horas.

Para uma aplicação pública, acrescente autenticação de usuários e persistência distribuída para o registro das salas.

## Arquitetura

- `Pages/Index.cshtml`: estúdio do apresentador;
- `Pages/Assistir.cshtml`: player dos espectadores;
- `Services/LiveKitTokenService.cs`: tokens JWT e permissões do LiveKit;
- `Services/LiveKitRoomRegistry.cs`: salas e chaves temporárias do apresentador;
- `Services/StreamingSocketManager.cs`: fallback de sinalização WebSocket;
- `wwwroot/js/studio.js`: captura, publicação e gravação;
- `wwwroot/js/viewer.js`: recepção da transmissão.

## Escalabilidade

Com LiveKit, os espectadores recebem a mídia pela SFU em vez de abrir uma conexão direta com o apresentador. Para gravação no servidor, retransmissão RTMP ou armazenamento permanente, o próximo passo é configurar o [LiveKit Egress](https://docs.livekit.io/home/egress/overview/).

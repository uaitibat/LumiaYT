# LumiaYT

Cliente YouTube estilo Metro para Windows 10 Mobile (Lumia 640, 15063).

## Status
MVP inicial: busca, trending, inscrições (OAuth oficial) e esqueleto de player.

## Configuração
1. Crie uma API key no Google Cloud para YouTube Data API v3.
2. Crie credenciais OAuth 2.0 (tipo adequado para UWP/WAB) e copie Client ID/Secret.
3. No app, abra **Configurações**, cole API key/Client ID/Secret e salve.
4. Toque em **Login Google**.

## Limitações conhecidas
- Feed "For You" personalizado não está disponível pela API oficial.
- Reprodução direta no app requer resolvedor de stream compatível com W10M (próximo passo).
- Lives podem exigir HLS compatível com o media stack do 15063.

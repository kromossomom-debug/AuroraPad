<div align="center">

# 🌌 AuroraPad

**Leve o seu DualSense para o PC de verdade.**
Gatilhos adaptativos, hápticos, barra de luz, touchpad como mouse, emulação de Xbox 360 / DualShock 4 e muito mais — num app leve, bonito e em português.

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-6C3FC7)
![Idiomas](https://img.shields.io/badge/idiomas-PT%20%7C%20EN%20%7C%20ES-2EA44F)
![Licença](https://img.shields.io/badge/licen%C3%A7a-MIT-informational)

<img src="docs/screenshots/home.png" alt="Tela inicial do AuroraPad" width="860" />

</div>

---

## ✨ Destaques

| | |
|---|---|
| 🎮 **Controle ao vivo** | Um DualSense desenhado na tela reage a tudo: botões acendem, analógicos se mexem, gatilhos enchem conforme a pressão, o touchpad mostra seus dedos e a barra de luz mostra a cor real. |
| 🔫 **Gatilhos adaptativos** | Resistência, arma semiautomática, rifle, arco, galope, máquina, vibração… com **gráfico da resistência ao longo do curso** e predefinições prontas (pistola, freio, acelerador, cavalgada). |
| 🖱️ **Touchpad como mouse** | Igual ao touchpad de um notebook: um dedo move, apertar clica (lado direito = clique direito), toque leve clica, dois dedos rolam. |
| 💥 **Vibração e hápticos HD** | Converte a vibração do jogo — ou o som do PC — em hápticos de alta definição pelos atuadores do DualSense (USB). Padrões de teste: pulso, explosão, motor, batimento, chuva. |
| 🌈 **Iluminação** | Barra de luz RGB com brilho, cor pela bateria, LEDs de jogador e LED do microfone. |
| 🕹️ **Emulação** | Vira um controle **Xbox 360** ou **DualShock 4** (com giroscópio e touchpad) via ViGEmBus. O HidHide esconde o controle físico para evitar entradas duplicadas. |
| ⌨️ **Teclado, mouse e macros** | Qualquer botão vira tecla, clique ou macro (`ctrl+c`, `w hold 300, wait 50, space`). Turbo por botão e cursor pelo giroscópio. |
| 🎯 **Perfis por jogo** | Biblioteca de jogos da **Steam, Epic e GOG**; o perfil certo entra sozinho quando o jogo abre. |
| 🔌 **Integrações** | Servidor UDP compatível com mods de gatilhos (porta 6969), servidor de movimento **Cemuhook/DSU** para emuladores (Cemu, Dolphin, Ryujinx…) e saída **OSC** (VRChat, TouchDesigner…). |
| ⚡ **Atalhos PS** | PS + Options/Create troca de perfil, PS + touchpad liga/desliga o mouse, PS + △ pausa o controle virtual, PS + ▲/▼ ajusta o brilho. |

## 📸 Telas

<table>
<tr>
<td><img src="docs/screenshots/live.png" alt="Entrada ao vivo" /><p align="center"><b>Entrada ao vivo</b></p></td>
<td><img src="docs/screenshots/triggers.png" alt="Gatilhos adaptativos" /><p align="center"><b>Gatilhos adaptativos</b></p></td>
</tr>
<tr>
<td><img src="docs/screenshots/keyboard-mouse.png" alt="Teclado, mouse e macros" /><p align="center"><b>Touchpad, mouse e macros</b></p></td>
<td><img src="docs/screenshots/haptics.png" alt="Vibração e hápticos" /><p align="center"><b>Vibração e hápticos</b></p></td>
</tr>
<tr>
<td colspan="2"><img src="docs/screenshots/emulation.png" alt="Emulação e conexões" /><p align="center"><b>Emulação e conexões</b></p></td>
</tr>
</table>

## 🎮 Controles suportados

| Controle | USB | Bluetooth | Gatilhos adaptativos | Hápticos por áudio |
|---|:---:|:---:|:---:|:---:|
| DualSense (PS5) | ✅ | ✅ | ✅ | ✅ (USB) |
| DualSense Edge | ✅ | ✅ | ✅ | ✅ (USB) |
| DualShock 4 (PS4) | ✅ | ✅ | — | — |

## 🚀 Começando

1. Baixe a versão mais recente em **Releases** (ou compile, veja abaixo) e rode `AuroraPad.exe`. Não precisa instalar o .NET.
2. Conecte o controle por cabo ou Bluetooth — ele aparece sozinho.
3. Opcional, para emular Xbox 360 / DualShock 4: instale o [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) (o app tem um botão para isso).
4. Opcional, para esconder o controle físico dos jogos: instale o [HidHide](https://github.com/nefarius/HidHide/releases).

### Compilar

Requer o [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet publish src/AuroraPad.App -c Release -r win-x64 --self-contained true -o publish
```

## 🔌 API UDP para mods

Envie JSON para `127.0.0.1:6969` (formato compatível com mods de gatilhos adaptativos já existentes):

```json
{"instructions":[{"type":1,"parameters":[0,2,13,0,8]}]}
```

| type | instrução | parâmetros |
|---|---|---|
| 1 | TriggerUpdate | controle, gatilho (1=L, 2=R), modo, ...extras |
| 2 | RGBUpdate | controle, R, G, B[, brilho] |
| 3 | PlayerLED | controle, 5 booleanos |
| 4 | TriggerThreshold | controle, gatilho, limiar 0-255 |
| 5 | MicLED | controle, 0=ligado 1=pulsando 2=desligado |
| 6 | PlayerLEDNewRevision | controle, 0-4 = jogador 1-5, 5 = apagado |
| 7 | ResetToUserSettings | controle |

Cada pacote recebe uma resposta JSON com o estado dos controles. Os números dos modos de gatilho estão em [`TriggerSettings.cs`](src/AuroraPad.Core/Triggers/TriggerSettings.cs).

## 📡 OSC

`/dsx/n/lx`, `ly`, `rx`, `ry`, `l2`, `r2` (float) · `/dsx/n/button/<nome>` (0/1, ao mudar) · `/dsx/n/gyro`, `/dsx/n/accel` (3 floats) · `/dsx/n/touch1`, `touch2` (ativo, x, y) · `/dsx/n/battery`

## 🧱 Arquitetura

```
src/
├─ AuroraPad.Core   protocolos HID (DualSense e DS4), gatilhos, hápticos, emulação,
│                   mapeamento/macros, touchpad-mouse, DSU, OSC, UDP, perfis, biblioteca
└─ AuroraPad.App    interface WPF, desenho do controle, traduções (PT/EN/ES)
tools/
└─ AuroraPad.Probe  testes no hardware: dsu, osc, haptics, tree, library, macro, hidhide
```

Perfis e configurações ficam em `%APPDATA%\AuroraPad`.

## ⚠️ Observações

- O mapeamento para teclado/mouse usa `SendInput`; alguns jogos com anticheat ignoram entrada sintética.
- Hápticos por áudio dependem da placa de som USB do DualSense — por Bluetooth só a vibração clássica funciona.
- O HidHide pode exigir rodar o AuroraPad como administrador.

## 📜 Aviso legal

AuroraPad é um projeto independente e **não é afiliado, endossado ou patrocinado pela Sony Interactive Entertainment**. "PlayStation", "DualSense" e "DualShock" são marcas registradas de seus respectivos donos e são citadas apenas para indicar compatibilidade. A ilustração do controle é original.

Construído com [HidSharp](https://www.zer7.com/software/hidsharp), [ViGEm.Client](https://github.com/nefarius/ViGEm.NET), [Nefarius.Drivers.HidHide](https://github.com/nefarius/Nefarius.Drivers.HidHide), [NAudio](https://github.com/naudio/NAudio) e [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).

Licenciado sob a [MIT License](LICENSE).

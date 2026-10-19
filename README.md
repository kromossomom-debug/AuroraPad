<div align="center">

<img src="assets/aurorapad-256.png" alt="AuroraPad" width="120" />

# 🌌 AuroraPad

**🇧🇷 Português** · [🇺🇸 English](README.en.md)

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
| 🖱️ **Touchpad como mouse** | Igual ao touchpad de um notebook: um dedo move, apertar clica (lado direito = clique direito), toque leve clica, dois dedos rolam. Só vira mouse depois de **dois toques ou de segurar o dedo** — o controle vibra para confirmar e trava sozinho quando você para de usar. |
| 💥 **Vibração e hápticos HD** | Converte a vibração do jogo — ou o som do PC, ou um **arquivo de áudio** — em hápticos de alta definição pelos atuadores do DualSense — no cabo USB e também sem fio, por Bluetooth. Padrões de teste: pulso, explosão, motor, batimento, chuva. Aviso na bandeja quando a **bateria fica fraca**. |
| 🔊 **Som do PC no controle** | Toca o som do computador no alto-falante do DualSense (ou no fone ligado nele) sem mudar a saída de som do Windows — no cabo USB e também sem fio, por Bluetooth. |
| 🌈 **Iluminação** | Barra de luz RGB com brilho, cor pela bateria, LEDs de jogador e LED do microfone. |
| 🕹️ **Emulação** | Vira um controle **Xbox 360** ou **DualShock 4** (com giroscópio e touchpad) via ViGEmBus. O HidHide esconde o controle físico para evitar entradas duplicadas. |
| ⌨️ **Teclado, mouse e macros** | Qualquer botão vira tecla, clique ou macro (`ctrl+c`, `w hold 300, wait 50, space`). Turbo por botão e cursor pelo giroscópio. |
| 👆 **Ativadores e camadas** | Cada botão pode agir no **toque, toque duplo ou ao segurar** (e o uso normal continua indo para o jogo), modo **alternar** (aperta uma vez, fica segurado) e uma **camada Shift** inteira de mapeamentos enquanto você segura um botão. |
| 🎯 **Mira avançada** | **Flick stick** (o analógico direito vira a câmera na hora), **giroscópio como analógico** para jogos sem suporte a mouse, suavização e **calibração automática** do giroscópio. |
| 🔫 **Gatilhos em dois estágios** | Meio curso e fim de curso viram botões separados: mire no meio, atire no fim. |
| 🎯 **Perfis por jogo** | Biblioteca de jogos da **Steam, Epic e GOG**; o perfil certo entra sozinho quando o jogo abre. |
| 🔌 **Integrações** | Servidor UDP compatível com mods de gatilhos (porta 6969), servidor de movimento **Cemuhook/DSU** para emuladores (Cemu, Dolphin, Ryujinx…) e saída **OSC** (VRChat, TouchDesigner…). |
| ⚡ **Atalhos PS** | PS + Options/Create troca de perfil, PS + touchpad liga/desliga o mouse, PS + △ pausa o controle virtual, PS + ▲/▼ ajusta o brilho. |
| 🪶 **Roda em segundo plano** | Fechar a janela deixa o AuroraPad na bandeja do sistema gastando pouquíssima memória; pode iniciar com o Windows direto na bandeja. |

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
| DualSense (PS5) | ✅ | ✅ | ✅ | ✅ (USB e Bluetooth) |
| DualSense Edge | ✅ | ✅ | ✅ | ✅ (USB e Bluetooth) |
| DualShock 4 (PS4) | ✅ | ✅ | — | — |

## 🚀 Começando

### Instalar

1. Baixe **`AuroraPad-<versão>-win-x64.msi`** na pasta [`release-files`](release-files) (ou em **[Releases](../../releases/latest)**).
2. Abra o instalador → **Avançar** → aceite a licença → pronto. Não precisa de administrador nem de .NET: ele instala em `%LOCALAPPDATA%\Programs\AuroraPad` e cria atalhos no Menu Iniciar e na Área de Trabalho.
3. Conecte o controle por cabo ou Bluetooth — ele aparece sozinho.

> Prefere sem instalar? Baixe **`AuroraPad-<versão>-win-x64-portable.zip`**, extraia e rode `AuroraPad.exe`.
>
> O Windows pode mostrar o aviso do SmartScreen porque o instalador não tem assinatura digital paga: clique em **Mais informações → Executar assim mesmo**.

Para desinstalar: **Configurações → Aplicativos → AuroraPad → Desinstalar** (perfis ficam em `%APPDATA%\AuroraPad`).

**Opcionais**
- Emular Xbox 360 / DualShock 4: instale o [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) (o app tem um botão para isso).
- Esconder o controle físico dos jogos: instale o [HidHide](https://github.com/nefarius/HidHide/releases).

### Compilar e gerar o instalador

Requer o [.NET 9 SDK](https://dotnet.microsoft.com/download). O WiX Toolset vem pelo NuGet, sem instalar nada.

```powershell
.\build-installer.ps1                  # versão do projeto
.\build-installer.ps1 -Version 1.3.0   # ou uma versão específica
```

Os arquivos saem em `artifacts\installer\*.msi` e `artifacts\*-portable.zip`.

### Publicar uma versão no GitHub

O workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) gera o instalador e cria a Release sozinho quando uma tag de versão é enviada:

```bash
git tag v1.2.0
git push origin v1.2.0
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
installer/          instalador MSI (WiX Toolset) — gerado por build-installer.ps1
assets/             ícone do app
```

Perfis e configurações ficam em `%APPDATA%\AuroraPad`.

## ⚠️ Observações

- O mapeamento para teclado/mouse usa `SendInput`; alguns jogos com anticheat ignoram entrada sintética.
- Por Bluetooth, os hápticos usam o relatório HID 0x32 e o som do PC no alto-falante do controle usa o relatório 0x36 (áudio Opus), sem precisar de cabo.
- O HidHide pode exigir rodar o AuroraPad como administrador.

## 📜 Aviso legal

AuroraPad é um projeto independente e **não é afiliado, endossado ou patrocinado pela Sony Interactive Entertainment**. "PlayStation", "DualSense" e "DualShock" são marcas registradas de seus respectivos donos e são citadas apenas para indicar compatibilidade. A ilustração do controle é original.

Construído com [HidSharp](https://www.zer7.com/software/hidsharp), [ViGEm.Client](https://github.com/nefarius/ViGEm.NET), [Nefarius.Drivers.HidHide](https://github.com/nefarius/Nefarius.Drivers.HidHide), [NAudio](https://github.com/naudio/NAudio), [Concentus](https://github.com/lostromb/concentus) e [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).

Licenciado sob a [MIT License](LICENSE).

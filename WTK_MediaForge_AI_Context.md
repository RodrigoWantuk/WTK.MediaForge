# WTK MediaForge — Documento de Contexto para Assistente de IA

Este documento serve como briefing técnico para uma IA continuar ajudando no desenvolvimento do **WTK MediaForge** sem precisar reconstruir todo o contexto do zero. Ele consolida as premissas, decisões de arquitetura, objetivos do projeto, estado atual da prova de conceito, problemas conhecidos e próximos passos recomendados.

O tom esperado da IA é técnico, direto e pragmático. O projeto está em fase de prova de conceito, mas as decisões devem apontar para uma arquitetura escalável, profissional e sustentável.

---

## 1. Identidade do Projeto

**Nome:** WTK MediaForge  
**Autor:** Rodrigo Wantuk  
**Contato comercial:** rodrigowantuk@gmail.com  
**Tipo de projeto:** source-available com licença não comercial e possibilidade de licenciamento comercial separado.

O WTK MediaForge é uma solução de composição de áudio e vídeo de alta performance, com foco em processamento em tempo real, aceleração por hardware, baixo uso de CPU e redução do tráfego desnecessário de frames brutos pela RAM.

A ideia central é construir um compositor de mídia **GPU-first**. O CPU deve coordenar o pipeline, controlar objetos, cenas, fontes, overlays e comandos, mas não deve ser responsável por processar todos os frames de vídeo em memória principal.

---

## 2. Objetivo Geral

Criar uma engine leve, modular e extensível para captura, composição, preview, gravação e transmissão de mídia em tempo real.

O projeto deve ser capaz, no longo prazo, de lidar com cenários como:

- captura de desktop;
- captura de região;
- captura de janela;
- captura de câmera;
- entradas de rede;
- overlays de imagem;
- overlays de texto;
- picture-in-picture;
- mosaicos de fontes;
- composição de cenas;
- roteamento e mixagem de áudio;
- preview em tempo real;
- gravação;
- streaming;
- controle dinâmico de cenas enquanto o pipeline está rodando;
- arquitetura futura de plugins ou módulos substituíveis.

O projeto não deve ser pensado como apenas uma tela de preview. A POC atual é apenas o primeiro passo para validar o pipeline GPU-first.

---

## 3. Premissas Técnicas Fundamentais

A premissa mais importante é evitar o caminho tradicional e pesado:

```text
Captura → frame bruto na RAM → Bitmap/byte[] → processamento CPU → cópia para GPU → renderização
```

Esse caminho deve ser evitado no pipeline principal.

O caminho desejado é:

```text
Captura por API nativa
→ recurso GPU
→ interoperabilidade GPU/GPU
→ composição por GPU
→ preview/encoder/output
```

Frames brutos de vídeo, especialmente em formatos como RGBA, BGRA ou NV12 em alta resolução, não devem ser copiados para RAM a cada frame como estratégia principal.

É aceitável manter em RAM:

- comandos;
- metadados;
- descrições de cena;
- posições;
- textos;
- configurações;
- pequenos buffers de controle;
- pacotes comprimidos, quando necessário;
- logs e métricas.

O que deve ser evitado:

- `Bitmap` para cada frame;
- `byte[]` contendo frame bruto para cada frame;
- `Map/Readback` da textura GPU para CPU no caminho principal;
- pipes de `rawvideo` como arquitetura central;
- conversões CPU de cor, escala, crop ou composição.

---

## 4. Plataforma Inicial

A plataforma inicial é Windows.

Stack atual:

- **.NET 8**;
- **WinForms** como host inicial da aplicação desktop;
- **Silk.NET** para bindings Vulkan;
- **Vulkan** para renderização/composição GPU;
- **Vortice.Windows** para D3D11/DXGI;
- **Desktop Duplication API** para a primeira captura de desktop;
- integração D3D11 → Vulkan via textura compartilhada e external memory.

O usuário desenvolve principalmente em C# WinForms e prefere avançar por etapas concretas, com código testável e diagnóstico claro.

---

## 5. Licença e Modelo de Distribuição

O projeto usa a licença:

```text
PolyForm Noncommercial License 1.0.0
```

A intenção é permitir uso pessoal, educacional, estudo, pesquisa, avaliação, hobby e outros usos não comerciais.

Uso comercial, industrial, broadcast, SaaS, revenda, consultoria, integração em produto pago, uso em produção ou qualquer uso gerador de receita deve exigir licença comercial separada.

O projeto deve ser descrito como:

```text
source-available
```

Evitar chamar o projeto de “open source” em sentido clássico, porque a licença não é aprovada como open source pela OSI e restringe uso comercial.

Texto de licença recomendado no README:

```text
WTK MediaForge is source-available under the PolyForm Noncommercial License 1.0.0.

You may use, study, modify, and run this project for personal, educational, research, evaluation, hobby, and other non-commercial purposes.

Commercial, industrial, SaaS, broadcast, resale, consulting, integration into paid products or services, production use, or any revenue-generating use requires a separate written commercial license from the author.

For commercial licensing, contact:

rodrigowantuk@gmail.com

Required Notice: Copyright Rodrigo Wantuk.
```

Arquivos recomendados na raiz do repositório:

```text
README.md
LICENSE.md
COMMERCIAL-LICENSE.md
THIRD_PARTY_NOTICES.md
CONTRIBUTING.md
NuGet.config
Directory.Packages.props
```

Terceiros não têm suas licenças substituídas pela licença do WTK MediaForge. Cada biblioteca deve manter sua licença própria documentada.

---

## 6. Dependências e Considerações de Licença

Dependências atuais ou planejadas:

```text
Silk.NET.Vulkan
Silk.NET.Vulkan.Extensions.KHR
Silk.NET.Vulkan.Extensions.EXT
Vortice.Direct3D11
Vortice.DXGI
Vortice.Mathematics
SkiaSharp, futuramente para texto/imagens se fizer sentido
FFmpeg, futuramente, com cuidado LGPL
```

Pontos importantes:

- Silk.NET é MIT/X11.
- Vortice.Windows é MIT.
- FFmpeg pode ser usado futuramente, mas a integração deve respeitar LGPL se essa for a escolha.
- Evitar FFmpeg compilado com `--enable-gpl` ou `--enable-nonfree` se o objetivo for manter uma distribuição mais simples.
- Evitar depender de `libx264`/`libx265` em builds redistribuídos se isso complicar licenciamento.
- Preferir encoders de hardware quando possível: NVENC, AMF, QSV, VideoToolbox, VAAPI, conforme plataforma.
- Para integração FFmpeg no futuro, avaliar `FFmpeg.AutoGen` ou uma ponte nativa C/C++ controlada.
- GStreamer foi discutido, mas a direção atual é **não usar GStreamer** neste momento.

---

## 7. Arquitetura da Solução

A solução foi simplificada para projetos separados por responsabilidade.

Estrutura atual/recomendada:

```text
WTK.MediaForge.sln

WTK.MediaForge.App.WinForms
WTK.MediaForge.Core
WTK.MediaForge.Capture
WTK.MediaForge.Graphics.D3D11
WTK.MediaForge.Graphics.Vulkan
WTK.MediaForge.Graphics.Interop
WTK.MediaForge.Composition
WTK.MediaForge.Diagnostics
```

### 7.1 WTK.MediaForge.Core

Projeto de contratos, tipos simples e modelos compartilhados.

Deve evitar dependências pesadas.

Contém ou deve conter:

```text
FrameSize
CaptureSourceInfo
GpuFrame abstractions
IRenderHost
interfaces comuns de fontes e outputs
modelos de cena genéricos
```

### 7.2 WTK.MediaForge.Capture

Responsável por fontes de captura.

No momento:

```text
Desktop Duplication API
monitor enumeration
desktop frame acquisition
```

Futuramente:

```text
region capture
window capture via Windows Graphics Capture
camera capture
stream input
file/media source
```

### 7.3 WTK.MediaForge.Graphics.D3D11

Encapsula device D3D11, recursos DXGI, texturas D3D11 e helpers relacionados.

Usa Vortice.

### 7.4 WTK.MediaForge.Graphics.Vulkan

Renderizador Vulkan.

Responsável por:

```text
instance
surface Win32
physical device selection
logical device
queues
swapchain
command buffers
sync objects
external memory import
render pipeline
shaders
composition pass
```

No momento a classe central de POC é `VulkanPreviewRenderer`.

### 7.5 WTK.MediaForge.Graphics.Interop

Deve concentrar, no futuro, a lógica entre APIs gráficas:

```text
D3D11 shared texture → Vulkan external memory
handle import
keyed mutex
LUID matching
format compatibility
resource lifetime
```

Hoje parte dessa lógica ainda está dentro do renderer e das classes de captura, mas deve migrar para cá quando amadurecer.

### 7.6 WTK.MediaForge.Composition

Deve representar a cena de mídia:

```text
Scene
Layer
Transform
Crop
Opacity
BlendMode
TextLayer
ImageLayer
VideoLayer
```

A composição deve ser comandada por dados e executada pela GPU.

### 7.7 WTK.MediaForge.Diagnostics

Métricas, timing e logs.

Exemplos:

```text
FPS
frame time
capture time
render time
present time
dropped frames
GPU backend information
interop errors
```

---

## 8. NuGet e Gerenciamento de Pacotes

Foi adotado `Directory.Packages.props` na raiz com `ManagePackageVersionsCentrally=true`.

Cada `.csproj` deve referenciar pacotes sem versão explícita.

Versões usadas/recomendadas na fase atual:

```text
Silk.NET.Vulkan 2.23.0
Silk.NET.Vulkan.Extensions.KHR 2.23.0
Silk.NET.Vulkan.Extensions.EXT 2.23.0
Vortice.Direct3D11 3.8.3
Vortice.DXGI 3.8.3
Vortice.Mathematics 2.1.1
SkiaSharp 3.119.4, reservado para uso futuro
```

Foi necessário criar `NuGet.config` na raiz para evitar conflito com feed DevExpress global e warning NU1507:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

A solução chegou a compilar com todos os projetos:

```text
8 succeeded
0 failed
```

---

## 9. Estado Atual da POC

A POC atual valida o seguinte caminho:

```text
Desktop Duplication API
→ ID3D11Texture2D capturada
→ textura D3D11 própria compartilhável
→ Shared NT Handle
→ Vulkan external memory import
→ dedicated allocation
→ keyed mutex
→ preview no WinForms Panel
```

O projeto de teste atual usa WinForms, com controles criados via designer.

Controles principais:

```text
cmbMonitors
btnStart
btnStop
lblStatus
pnlPreview
txtOverlay
timerCapture
```

Namespace/projeto de teste usado pelo usuário:

```text
WMF.Testing
Form1
```

O renderer atual é:

```text
VulkanPreviewRenderer
```

`VulkanSmokeTest` foi usado inicialmente para provar que Vulkan conseguia criar surface no painel WinForms, mas a POC atual usa `VulkanPreviewRenderer`.

---

## 10. O que Já Funcionou

Já foi validado:

```text
WinForms Panel.Handle → Vulkan surface
GPU Vulkan detectada
Swapchain Vulkan funcionando
Desktop Duplication capturando a ~60 FPS
D3D11 texture sendo obtida
Shared handle sendo criado
Vulkan importando recurso externo
Vulkan apresentando algo no painel
D3D11 → Vulkan interop funcionando parcialmente
```

No monitor principal, após adicionar **dedicated allocation**, a imagem passou a aparecer corretamente, sem as linhas/colunas quebradas que existiam antes.

Isso confirmou que o problema anterior não era a captura em si, mas a forma de importação/uso da memória externa.

---

## 11. Decisões Técnicas Importantes Já Tomadas

### 11.1 Desktop Duplication para a primeira captura

A primeira fonte é desktop inteiro via Desktop Duplication API.

Motivo:

```text
Ela entrega ID3D11Texture2D, ou seja, um recurso GPU.
```

Isso combina com a premissa GPU-first.

### 11.2 Região será crop, não captura CPU

Para capturar uma região do desktop, a ideia inicial é capturar o monitor inteiro e aplicar crop no compositor/shader.

Não fazer:

```text
capturar monitor inteiro → copiar para RAM → recortar CPU
```

Fazer:

```text
capturar monitor inteiro → textura GPU → UV/crop no shader
```

### 11.3 Janela específica futuramente via Windows Graphics Capture

Desktop Duplication captura monitor. Para captura de janela específica no futuro, o caminho mais adequado provavelmente será Windows Graphics Capture.

### 11.4 FFmpeg só depois

A POC atual não deve incluir encoder, RTSP, gravação ou streaming.

Primeiro objetivo:

```text
capturar desktop → importar no Vulkan → renderizar preview corretamente
```

Depois:

```text
composição
texto
fontes múltiplas
encoder
saída
```

### 11.5 `vkCmdCopyImage` é POC, não renderer final

O caminho atual com `vkCmdCopyImage` serviu para provar o interop.

Ele não deve ser considerado a arquitetura final do compositor.

Motivo:

```text
vkCmdCopyImage só copia pixels.
Ele não resolve escala, crop, aspect ratio, rotação, PiP, composição, texto, blend ou efeitos.
```

O próximo passo correto é samplear a textura importada via shader.

---

## 12. Problema Atual

Monitor 1 funciona corretamente após dedicated allocation.

Monitor 2, que está em orientação retrato, continua preto.

O monitor 2 provavelmente envolve um ou mais dos seguintes pontos:

```text
rotação do display
textura real da Desktop Duplication diferente do tamanho lógico do monitor
tratamento incompleto da rotação no renderer
source reimportada em dimensões/formato diferentes
layout/sync ao trocar fonte
uso de vkCmdCopyImage sem pipeline de amostragem
```

A Desktop Duplication trabalha com superfícies que podem não vir na orientação lógica do monitor. Em monitores rotacionados, a textura pode vir “deitada”, com a imagem rotacionada dentro dela.

Exemplo esperado:

```text
Monitor lógico em retrato: 1024x1280
Textura real da duplicação: 1280x1024
Rotação: 90 graus
```

Sem shader, não há uma boa forma de corrigir isso corretamente.

---

## 13. Diagnóstico do Problema das Linhas Quebradas

Antes da dedicated allocation, o monitor 1 aparecia com linhas, blocos repetidos e colunas sobrepostas.

Isso parecia indicar que a memória da textura importada estava sendo interpretada de forma errada pelo Vulkan.

Foi sugerido adicionar `MemoryDedicatedAllocateInfo` na cadeia de alocação da external memory.

Após isso, a imagem do monitor 1 ficou correta.

Conclusão:

```text
External image import de D3D11 para Vulkan precisava de dedicated allocation.
```

Esse ponto deve permanecer na arquitetura.

---

## 14. Cadeia Correta de Importação de Memória Externa

A importação atual deve usar uma cadeia semelhante a:

```text
MemoryAllocateInfo
  → MemoryDedicatedAllocateInfo
      → ImportMemoryWin32HandleInfoKHR
```

Exemplo conceitual:

```csharp
var importMemoryInfo = new ImportMemoryWin32HandleInfoKHR
{
    SType = StructureType.ImportMemoryWin32HandleInfoKhr,
    HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
    Handle = sharedHandle
};

var dedicatedAllocateInfo = new MemoryDedicatedAllocateInfo
{
    SType = StructureType.MemoryDedicatedAllocateInfo,
    PNext = &importMemoryInfo,
    Image = _sourceImage,
    Buffer = default
};

var allocateInfo = new MemoryAllocateInfo
{
    SType = StructureType.MemoryAllocateInfo,
    PNext = &dedicatedAllocateInfo,
    AllocationSize = memoryRequirements.Size,
    MemoryTypeIndex = FindMemoryType(
        memoryRequirements.MemoryTypeBits,
        MemoryPropertyFlags.DeviceLocalBit)
};
```

Esse detalhe foi decisivo para corrigir o monitor 1.

---

## 15. Keyed Mutex

A textura D3D11 compartilhada usa:

```text
ResourceOptionFlags.SharedNthandle
ResourceOptionFlags.SharedKeyedmutex
```

O D3D11 deve usar `IDXGIKeyedMutex` para sincronizar escrita com leitura Vulkan.

No Vortice, os métodos `AcquireSync` e `ReleaseSync` retornam `void`, não `Result`.

Logo, não usar:

```csharp
var acquireResult = _keyedMutex.AcquireSync(0, 1000);
```

Usar:

```csharp
_keyedMutex.AcquireSync(0, 1000);
```

E:

```csharp
_keyedMutex.ReleaseSync(1);
```

Conceito atual:

```text
D3D11 acquire key 0
D3D11 CopyResource para textura compartilhada
D3D11 Flush
D3D11 release key 1
Vulkan acquire key 1
Vulkan lê/renderiza
Vulkan release key 0
```

O Vulkan deve habilitar `VK_KHR_win32_keyed_mutex` e usar `Win32KeyedMutexAcquireReleaseInfoKHR` no submit.

---

## 16. Estado do `DesktopDuplicationCaptureSource`

A classe de captura faz, conceitualmente:

```text
seleciona adapter/output
cria D3D11 device para o adapter
chama DuplicateOutput
cria textura própria compartilhável
cria shared NT handle
obtém keyed mutex
AcquireNextFrame
QueryInterface para ID3D11Texture2D
CopyResource para textura própria
Flush
ReleaseFrame
retorna D3D11TextureFrame com texture, sharedHandle, size, frameNumber, timestamp
```

A textura própria existe porque a textura retornada pela Desktop Duplication não deve ser usada diretamente como recurso compartilhado de longa duração com Vulkan.

A textura própria precisa ser:

```text
D3D11 usage default
bind shader resource/render target
sem CPUAccess
shared NT handle
shared keyed mutex
formato compatível
mesmo tamanho real da duplicação
```

Tamanho e formato precisam ser obtidos da duplicação ou do frame real, mas com cuidado para não quebrar o lifecycle da importação Vulkan.

---

## 17. Estado do `D3D11TextureFrame`

A estrutura atual representa um frame GPU D3D11.

Ela contém:

```text
ID3D11Texture2D Texture
nint SharedHandle
bool HasSharedHandle
FrameSize Size
long FrameNumber
long Timestamp
```

Exemplo conceitual:

```csharp
public sealed class D3D11TextureFrame
{
    public D3D11TextureFrame(
        ID3D11Texture2D texture,
        nint sharedHandle,
        FrameSize size,
        long frameNumber,
        long timestamp)
    {
        Texture = texture;
        SharedHandle = sharedHandle;
        Size = size;
        FrameNumber = frameNumber;
        Timestamp = timestamp;
    }

    public ID3D11Texture2D Texture { get; }
    public nint SharedHandle { get; }
    public bool HasSharedHandle => SharedHandle != 0;
    public FrameSize Size { get; }
    public long FrameNumber { get; }
    public long Timestamp { get; }
}
```

---

## 18. Estado do `VulkanPreviewRenderer`

O `VulkanPreviewRenderer` já faz:

```text
criação de instance Vulkan
criação de surface Win32 a partir do HWND do Panel
seleção de physical device com graphics+present
criação de logical device
habilitação de extensões externas
criação de swapchain
criação de command pool/buffer
criação de semaphores/fence
importação de textura D3D11 via shared handle
alocação de memória externa com dedicated allocation
sincronização com keyed mutex
present no painel
```

Extensões importantes:

```text
VK_KHR_swapchain
VK_KHR_external_memory
VK_KHR_external_memory_win32
VK_KHR_win32_keyed_mutex
```

O renderer atualmente usa uma estratégia temporária:

```text
D3D11 imported image → vkCmdCopyImage → swapchain image
```

Isso deve ser substituído por pipeline com shader.

---

## 19. Próximo Passo Técnico Recomendado

Próximo passo principal:

```text
Substituir vkCmdCopyImage por renderização com shader sampleando a textura importada.
```

Fluxo desejado:

```text
D3D11 shared texture
→ Vulkan imported image
→ Vulkan ImageView
→ Vulkan Sampler
→ DescriptorSetLayout
→ DescriptorPool
→ DescriptorSet
→ RenderPass ou dynamic rendering
→ Pipeline gráfico
→ Fullscreen triangle
→ Fragment shader sampleia textura
→ Swapchain
```

Isso é o início real do compositor.

---

## 20. Por Que Ir Para Shader Agora

O usuário inicialmente questionou se era necessário passar por shaders ou gradiente fixo, porque parecia um desvio.

A resposta correta agora é:

```text
Sim, agora shaders são necessários.
Não como demo inútil, mas como renderer real da textura capturada.
```

Razões:

- `vkCmdCopyImage` não escala;
- `vkCmdCopyImage` não corrige aspect ratio;
- `vkCmdCopyImage` não rotaciona;
- `vkCmdCopyImage` não faz crop;
- `vkCmdCopyImage` não compõe múltiplas camadas;
- `vkCmdCopyImage` não aplica alpha;
- `vkCmdCopyImage` não desenha texto;
- `vkCmdCopyImage` não prepara o projeto para PiP/mosaico;
- `vkCmdCopyImage` é frágil em interop external image;
- shader é o caminho natural para um compositor GPU.

A mudança para shader não deve ser apresentada como gambiarra, mas como a evolução correta da POC.

---

## 21. Fullscreen Triangle

Para preview inicial, preferir fullscreen triangle a quad com vertex buffer.

Vantagens:

```text
não precisa vertex buffer
menos estado
simples
bom para tela cheia
```

Vertex shader conceitual:

```glsl
#version 450

layout(location = 0) out vec2 vUv;

void main()
{
    vec2 positions[3] = vec2[](
        vec2(-1.0, -1.0),
        vec2( 3.0, -1.0),
        vec2(-1.0,  3.0)
    );

    vec2 uvs[3] = vec2[](
        vec2(0.0, 1.0),
        vec2(2.0, 1.0),
        vec2(0.0, -1.0)
    );

    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
    vUv = uvs[gl_VertexIndex];
}
```

Fragment shader inicial:

```glsl
#version 450

layout(set = 0, binding = 0) uniform sampler2D uDesktopTexture;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

void main()
{
    outColor = texture(uDesktopTexture, vUv);
}
```

Depois, o shader pode aplicar rotação/crop/aspect ratio.

---

## 22. Tratamento de Rotação

Monitor em retrato deve ser tratado por rotação de UV no shader ou por matriz de transformação.

Exemplos conceituais:

```glsl
// sem rotação
vec2 uv = vUv;

// rotação 90 graus
vec2 uv = vec2(vUv.y, 1.0 - vUv.x);

// rotação 270 graus
vec2 uv = vec2(1.0 - vUv.y, vUv.x);

// rotação 180 graus
vec2 uv = vec2(1.0 - vUv.x, 1.0 - vUv.y);
```

Idealmente, a rotação deve vir da descrição do output/duplicação, não ser hardcoded.

No futuro, `CaptureSourceInfo` deve carregar:

```text
logical width/height
texture width/height
rotation
desktop coordinates
adapter/output indices
adapter LUID
format
```

---

## 23. Aspect Ratio, Fit e Fill

O preview não deve simplesmente esticar a textura sem critério.

Modos futuros:

```text
Stretch
Fit / contain
Fill / cover
Crop
Center
Custom transform
```

Para preview inicial, usar `Fit` com barras se necessário, preservando proporção.

Isso deve ser feito via cálculo de UV ou push constants/uniforms.

---

## 24. Text Overlay

A UI já possui `txtOverlay`, mas no momento o texto ainda não é renderizado pelo Vulkan.

A fase futura deve transformar texto em textura ou geometria.

Caminhos possíveis:

```text
SkiaSharp renderiza texto em bitmap pequeno → upload para textura Vulkan quando texto muda
atlas de fontes
DirectWrite/Direct2D interop
SDF fonts
```

Para começar, é aceitável usar SkiaSharp ou outra solução CPU para rasterizar apenas o overlay quando o texto mudar, porque isso não viola a premissa central: o frame de vídeo principal continua GPU-first.

O que deve ser evitado é rasterizar/compor todo o frame de vídeo por CPU.

---

## 25. Como o Projeto Deve Escalar

O projeto deve evoluir em camadas.

### Fase 1 — POC de captura e preview

Objetivo:

```text
Desktop Duplication → D3D11 texture → Vulkan preview
```

Status:

```text
Monitor 1 funciona
Monitor 2 retrato ainda preto
copy direto ainda é temporário
```

### Fase 2 — Shader preview real

Objetivo:

```text
Samplear textura capturada no fragment shader
corrigir escala/aspect ratio
corrigir retrato/rotação
```

### Fase 3 — Compositor básico

Objetivo:

```text
Scene
Layer
Transform
Opacity
Text overlay simples
Image overlay simples
```

### Fase 4 — Fontes múltiplas

Objetivo:

```text
Desktop 1
Desktop 2
região
janela
imagem
texto
câmera
```

### Fase 5 — Saídas

Objetivo:

```text
preview
recording
streaming
virtual output, se desejado
```

### Fase 6 — Encoder

Objetivo:

```text
hardware encoding
FFmpeg integration
NVENC/AMF/QSV
controle de bitrate/framerate/formato
```

### Fase 7 — Arquitetura de produto

Objetivo:

```text
configurações persistentes
presets
projetos/sessões
plugin/module API
UI final
licenciamento comercial
instalador
telemetria opcional/local diagnostics
```

---

## 26. Filosofia de Desenvolvimento

O usuário prefere avançar com passos práticos e verificáveis.

A IA deve evitar grandes refactors sem necessidade imediata.

Boa abordagem:

```text
1. explicar objetivo do patch
2. indicar arquivos exatos
3. fornecer código pequeno e compilável
4. explicar como testar
5. interpretar o resultado
6. só então avançar
```

Evitar:

```text
- sugerir trocar tudo para outra stack sem necessidade
- propor GStreamer agora
- propor Electron/web se o objetivo atual é WinForms/Vulkan
- voltar para Bitmap/CPU
- sugerir FFmpeg rawvideo para preview
- recomeçar a arquitetura do zero
- responder genericamente sem código
```

Quando houver erro de compilação, pedir ou usar:

```text
mensagem exata
linha exata
arquivo
assinatura esperada pela versão atual da biblioteca
```

O usuário costuma colar erros do Visual Studio. A resposta ideal deve adaptar o código à API real do Vortice/Silk.NET que está em uso.

---

## 27. Regras Práticas para a IA no Projeto

Ao sugerir código Vulkan/Silk.NET:

- lembrar que muitos structs usam ponteiros e `unsafe`;
- evitar usar nomes de enums obsoletos quando possível;
- conferir nomes reais do Silk.NET 2.23.0;
- usar `stackalloc` para arrays pequenos temporários;
- cuidado ao pegar endereço de campos de classe;
- C# não permite `&_commandBuffer` se `_commandBuffer` é campo; usar variável local ou stackalloc;
- liberar strings alocadas com `SilkMarshal.StringToPtr`;
- não assumir que existe `SilkMarshal.FreeStringArrayToPtr`;
- não deixar recursos Vulkan sem destroy;
- chamar `DeviceWaitIdle` antes de destruir/recriar swapchain/source;
- separar `Resize` de `SetSource`.

Ao sugerir código D3D11/Vortice:

- `IDXGIKeyedMutex.AcquireSync` e `ReleaseSync` podem retornar `void`;
- `CopyResource` exige origem e destino compatíveis;
- usar `SharedNthandle` para NT handle;
- fechar handle com `CloseHandle` quando apropriado;
- limpar keyed mutex antes da textura;
- respeitar `ReleaseFrame` em `finally`.

---

## 28. Problemas Conhecidos e Possíveis Causas

### 28.1 Monitor em retrato preto

Possíveis causas:

```text
rotação não tratada
source image com dimensões diferentes do esperado
vkCmdCopyImage inadequado para esse caso
layout da imagem importada
source não sendo reimportada corretamente
Vulkan e D3D usando adapters diferentes, em setups multi-GPU
```

Próxima ação recomendada:

```text
ir para shader sampleando textura, com rotação por UV
```

### 28.2 Linhas e colunas quebradas

Já foi mitigado/corrigido no monitor 1 adicionando dedicated allocation.

Manter essa correção.

### 28.3 Espelho infinito

Se o app exibe no mesmo monitor que está capturando, haverá efeito de espelho infinito.

Isso é normal e não deve ser confundido com bug.

Mas quando o app está em outro monitor e ainda há linhas quebradas, é bug de importação/renderização.

### 28.4 Queda de FPS

Se FPS cai muito ao ativar import/copy/render, investigar:

```text
WaitIdle excessivo
reimportação por frame
keyed mutex bloqueando
DeviceWaitIdle dentro do loop
recriação de source sem necessidade
fences mal usadas
validation/debug layer
```

`SetSourceD3D11SharedTexture` não deve reimportar se handle/tamanho/formato não mudaram.

---

## 29. Checklist Antes de Avançar para Shader

Antes ou durante a migração para shader, validar:

```text
ClearSource chamado ao trocar de monitor
source destruída com DeviceWaitIdle
shared handle comparado corretamente
source image criada uma vez por source
não reimportar todo frame
swapchain format preferencialmente B8G8R8A8Unorm para copy temporário
source format compatível com ImageView/Sampler
ImageUsage inclui SampledBit
layout da source adequado para shader read
descriptor atualizado após importação
keyed mutex no submit Vulkan
```

---

## 30. Mudança Recomendada no Renderer

A mudança mais importante agora é criar um renderer mínimo com pipeline gráfico.

Componentes necessários:

```text
source ImageView
source Sampler
descriptor set layout
descriptor pool
descriptor set
render pass ou dynamic rendering
pipeline layout
graphics pipeline
shader modules
command buffer desenhando fullscreen triangle
```

Sequência do command buffer:

```text
begin command buffer
transition swapchain image para color attachment, se necessário
begin render pass/dynamic rendering
bind pipeline
bind descriptor set da source texture
draw 3 vertices
end render pass
transition swapchain image para present
end command buffer
```

A source image deve estar em layout compatível com shader read:

```text
ShaderReadOnlyOptimal
```

ou outro layout válido para o caso de external memory, conforme testes. Se `General` for necessário por causa do recurso externo, usar `General` e ajustar descriptor image layout de acordo.

---

## 31. Rotação e UV no Shader

A forma mais simples de resolver retrato é aplicar rotação no fragment shader ou no vertex shader.

O renderer deve receber um parâmetro de rotação:

```text
None
Rotate90
Rotate180
Rotate270
```

E transformar UV.

Exemplo conceitual:

```glsl
vec2 ApplyRotation(vec2 uv, int rotation)
{
    if (rotation == 1)
        return vec2(uv.y, 1.0 - uv.x);

    if (rotation == 2)
        return vec2(1.0 - uv.x, 1.0 - uv.y);

    if (rotation == 3)
        return vec2(1.0 - uv.y, uv.x);

    return uv;
}
```

No futuro, isso deve ser parte de uma matriz de transformação por layer.

---

## 32. Captura, Composição e Output Como Pipeline

Pensar o projeto como pipeline:

```text
Sources
→ GPU Resources
→ Scene Graph
→ Composition Passes
→ Preview Outputs
→ Encoder Outputs
→ Network/File Outputs
```

Exemplo futuro:

```text
DesktopCaptureSource produces GpuTextureFrame
CameraCaptureSource produces GpuTextureFrame
ImageSource produces static GpuTexture
TextSource produces texture atlas/overlay texture
CompositionEngine renders Scene to RenderTarget
PreviewOutput presents RenderTarget
EncoderOutput consumes RenderTarget using hardware encoder
```

---

## 33. Abstrações Futuras Úteis

Possíveis interfaces/modelos:

```csharp
public interface IMediaSource
{
    string Id { get; }
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync();
}

public interface IVideoSource : IMediaSource
{
    bool TryGetLatestFrame(out GpuVideoFrame frame);
}

public sealed class GpuVideoFrame
{
    public required FrameSize Size { get; init; }
    public required PixelFormat Format { get; init; }
    public required long FrameNumber { get; init; }
    public required long Timestamp { get; init; }
    public required object NativeResource { get; init; }
}
```

Mas não implementar cedo demais. Primeiro consolidar a POC.

---

## 34. Cuidados com Multi-GPU

No futuro, comparar o adapter D3D11 usado na captura com o physical device Vulkan.

Em setups com iGPU + dGPU, pode haver problema se:

```text
D3D11 captura em um adapter
Vulkan seleciona outro physical device
```

O ideal é casar por LUID.

A POC atual provavelmente está no mesmo GPU, mas isso deve ser tratado antes de produto.

---

## 35. Nome e Posicionamento

O nome atual é:

```text
WTK MediaForge
```

Descrição curta:

```text
A GPU-first audio and video composition engine for real-time capture, preview, overlays, recording, and streaming.
```

Descrição em português:

```text
Uma engine de composição de áudio e vídeo GPU-first para captura, preview, overlays, gravação e transmissão em tempo real.
```

Evitar posicionar como clone direto de OBS, mesmo que existam semelhanças. O diferencial desejado é arquitetura modular e controle próprio, com foco em baixo overhead e integração programável.

---

## 36. README Inicial Recomendado

O README deve explicar:

```text
o que é o projeto
objetivos
estado atual
licença
uso não comercial
licença comercial
stack técnica
roadmap
third-party notices
```

O projeto ainda é POC, então o README deve evitar prometer recursos prontos que ainda não existem.

Usar linguagem como:

```text
The current proof of concept focuses on...
Future versions may include...
The long-term goal is...
```

---

## 37. Coisas Que a IA Deve Lembrar Sobre o Usuário

O usuário:

- é programador C# WinForms;
- usa Visual Studio;
- prefere respostas em português;
- quer avançar rápido, mas aceita explicações técnicas quando justificadas;
- quer evitar desperdício de tempo com etapas “demo” que não levam ao produto;
- valoriza arquitetura correta, mas não quer overengineering cedo demais;
- gosta de ver código concreto;
- testa os patches e retorna prints/erros;
- prefere que a IA seja honesta quando algo é POC, gambiarra, temporário ou arquitetura final.

A IA deve ser direta ao dizer:

```text
isso é POC
isso é correto
isso é temporário
isso deve ser substituído depois
```

---

## 38. Resumo Executivo do Estado Atual

O WTK MediaForge está na fase de POC de captura/renderização.

Já foi alcançado:

```text
Desktop Duplication API capturando desktop
D3D11 textura compartilhável
shared NT handle
Vulkan external memory import
dedicated allocation corrigindo imagem quebrada
preview Vulkan no WinForms
monitor principal funcionando corretamente
```

Ainda falta:

```text
monitor em retrato funcionando
renderização via shader
tratamento de rotação
tratamento de aspect ratio
overlay de texto real
arquitetura formal de cena/layers
encoder/output
```

A próxima etapa correta é:

```text
substituir vkCmdCopyImage por pipeline Vulkan com shader sampleando a textura capturada.
```

Essa etapa transforma a POC de “interop funcionando” para “renderer/compositor inicial funcionando”.

---

## 39. Próxima Resposta Ideal da IA Se o Usuário Pedir Continuação

Se o usuário pedir para continuar, a IA deve propor uma sequência incremental:

```text
1. Criar shaders desktop_preview.vert e desktop_preview.frag.
2. Adicionar mecanismo simples para carregar SPIR-V ou compilar shader offline.
3. Criar ImageView para _sourceImage.
4. Criar Sampler.
5. Criar DescriptorSetLayout/Pool/Set.
6. Criar pipeline gráfico mínimo fullscreen triangle.
7. Trocar RecordCopyOrClearCommandBuffer por RecordRenderCommandBuffer.
8. Renderizar desktop no monitor 1.
9. Adicionar parâmetro de rotação.
10. Corrigir monitor 2 em retrato.
```

Não começar por overlays, encoder ou FFmpeg antes de estabilizar isso.

---

## 40. Frase Guia do Projeto

A frase que resume a direção técnica:

```text
O WTK MediaForge deve mover pixels de vídeo pela GPU, não pela RAM, e compor cenas em tempo real com o mínimo de overhead possível.
```


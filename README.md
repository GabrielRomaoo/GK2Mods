# GK2Mods

Mods de Graveyard Keeper 2 (BepInEx 5, jogo 1.011).

- `src/StorageNetwork` — **Rede de Armazenamento**: Arquivo Central + Relés de Zona + Conectores unificam os baús para as bancadas conectadas.
- `tools/DataDump` — plugin de desenvolvimento: exporta construções, tecnologias, baús e itens para `BepInEx/dumps` (F9 exporta o save carregado).

## Build

`dotnet build -c Release` em cada projeto. O build copia o mod para
`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\Code\<Mod>\`, que o jogo instala sozinho
na próxima abertura (contrato em `<jogo>\README_MODDING.txt`). Pasta com `~` no início do nome = mod desativado.

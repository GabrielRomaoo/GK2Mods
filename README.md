# GK2Mods

Mods para **Graveyard Keeper 2** (BepInEx 5, jogo 1.011+).

| Mod | O que faz |
|---|---|
| [Rede de Armazenamento](#rede-de-armazenamento) (`src/StorageNetwork`) | Junta os baús da base num estoque compartilhado que bancadas, o Coveiro e construções usam de qualquer zona. |
| DataDump (`tools/DataDump`) | Ferramenta de desenvolvimento: exporta construções, tecnologias, baús e itens do jogo para JSON. Não é para jogar. |

---

## Rede de Armazenamento

Chega de correr de baú em baú. A Rede liga os baús da sua base a um **Armazenamento Central**, e as bancadas
conectadas passam a usar esses materiais como se estivessem do lado delas. Ela não faz nada sozinha
nem cria itens: só tira o vai-e-vem de carregar coisas.

### Instalação

O jogo já vem com BepInEx e com o sistema oficial de mods, então não precisa instalar mais nada.

1. Baixe `GK2StorageNetwork-<versão>.zip` em [Releases](../../releases).
2. Extraia a pasta `GK2StorageNetwork` dentro de
   `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\Code\`
   (cole esse caminho na barra do Explorador de Arquivos; crie a pasta `Code` se não existir).
3. Abra o jogo. Ele instala o mod sozinho; a Rede de Armazenamento aparece na janela de Mods.

Para desativar, renomeie a pasta para `~GK2StorageNetwork` ou desligue o mod na janela de Mods. Os baús continuam
existindo: sem o mod, as três estruturas viram baús comuns, sem perder nenhum item.

### As três estruturas

| Estrutura | Como liberar | Custo padrão | O que faz |
|---|---|---|---|
| **Armazenamento Central** | Tecnologia *Articulação de Madeira* (árvore de Construção, mid game) | 4 articulações de madeira, 8 tábuas untadas, 4 engrenagens de bronze, 8 pregos de ferro | Coração da rede. Os baús da zona dele viram o estoque da rede. Só existe um por save. Também é um baú, e ao abrir mostra o estoque da rede inteira por categoria, de onde dá para retirar e depositar. |
| **Conector** | Terminar a obra do Armazenamento Central | 4 tábuas, 4 pregos de ferro, 1 engrenagem de bronze | Coloque perto de uma bancada, em **qualquer zona**: ela passa a usar o estoque da rede. A zona inteira também pode pagar construções com a rede. E é uma **caixa de envio**: o que você guardar nele vai para o Armazenamento Central uma vez por dia. |
| **Relé de Zona** | Tecnologia *Escultura em Madeira* (late game) | 2 madeiras entalhadas, 6 tábuas untadas, 4 detalhes de ferro, 2 engrenagens de bronze | Coloca os baús da zona dele no estoque da rede. A partir daí, o que é produzido ou guardado nessa zona fica disponível para todas as bancadas conectadas. |

### Como usar, passo a passo

1. **Mid game:** pesquise *Articulação de Madeira* e construa o **Armazenamento Central** na zona onde você guarda
   mais coisas. Todos os baús dessa zona passam a ser o estoque da rede.
2. Quando a obra terminar, você aprende o **Conector**. Ao escolher onde colocá-lo, as áreas azuis mostram
   quais bancadas ele alcança (alcance padrão: 4 células). Bancadas dentro da área usam o estoque da rede.
3. Abasteça o estoque levando itens para a zona do Armazenamento, ou guarde-os em qualquer Conector: um
   mensageiro leva tudo ao Armazenamento Central quando começa um novo dia.
4. **Late game:** com *Escultura em Madeira*, construa **Relés** nas outras zonas (jardim, oficina, necrotério…).
   Os baús delas entram no estoque, e você não precisa mais levar nada a lugar nenhum.

### Regras importantes

- **Sem Armazenamento Central pronto, nada funciona.** Relés e Conectores ficam esperando.
- **O que uma bancada produz fica na zona dela.** Para entrar no estoque, guarde num baú de uma zona da rede ou
  num Conector.
- **A fábrica fica de fora.** Nas zonas da linha de montagem (esteiras), Armazenamento, Relé e Conector não ligam
  nada, e as máquinas da fábrica nunca puxam da rede. A fábrica continua com a logística própria; o que ela
  produz chega à base pelos carregadores do jogo.
- **A Caserna não tem baús no menu de construção**, então não aceita Conector. Mesmo assim, barricadas, torres e
  expansões de lá são pagas com a rede assim que o Armazenamento Central estiver pronto.
- **Zumbis continuam como no jogo original.** Carregadores e fornecedores não usam a rede. Já um zumbi
  trabalhando numa bancada conectada usa.
- **O mensageiro não leva bolsas nem itens com estado próprio** (ferramentas com durabilidade, por exemplo). Eles
  ficam no Conector até você tirá-los. Se o estoque estiver cheio, o que não couber também fica.

### Configuração

Arquivo `<pasta do jogo>\BepInEx\config\gk2mods.storagenetwork.cfg`, criado na primeira abertura com o mod.
Edite com o jogo fechado.

| Opção | Padrão | Para que serve |
|---|---|---|
| `1 Unlock / Tech` | `wooden_joint` | Tecnologia que libera o Armazenamento Central. |
| `1 Unlock / RelayTech` | `tech_carved_wood` | Tecnologia que libera o Relé de Zona. |
| `2 Costs / CentralArchive`, `ZoneRelay`, `Connector` | ver tabela acima | Custo de cada estrutura, no formato `id_do_item:quantidade,...`. |
| `3 Network / ConnectorRadiusCells` | `4` | Alcance do Conector, em células da grade de construção (1 a 12). |
| `3 Network / KeeperBuildsFromNetwork` | `true` | Construções do Coveiro nas zonas da rede podem ser pagas com o estoque. |
| `3 Network / ExcludedZones` | `conveyor,conveyor_storage` | Zonas onde a rede não funciona (por padrão, a fábrica). |
| `3 Network / BuildZonesWithoutConnector` | `town_guard_barracks` | Zonas onde não dá para construir Conector, mas as construções são pagas com a rede assim que o Armazenamento Central estiver pronto (por padrão, a Caserna). |
| `9 Debug / Log` | `false` | Mensagens extras em `BepInEx\LogOutput.log`. |
| `9 Debug / TestMode` | `false` | Só para testes: estruturas sem tecnologia e custando 1 tábua. Use num save de teste. |

### Compatibilidade

- Feito para a versão 1.011 do jogo. Atualizações do jogo podem desativar partes do mod; o log avisa qual.
- Pode ser adicionado num save em andamento. Pode ser removido também: as estruturas viram baús comuns.

---

## Para desenvolvedores

Requisitos: .NET SDK (qualquer versão recente, o alvo é `net472`) e o jogo instalado. Se o jogo não estiver em
`C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2`, passe `-p:GameDir="<caminho>"`.

```
cd src/StorageNetwork
dotnet build -c Release
```

O build já copia o mod para `...\Mods\Code\GK2StorageNetwork\` (com `modinfo.json`), e o jogo instala na próxima
abertura (contrato em `<jogo>\README_MODDING.txt`). Os ícones e modelos 3D são gerados pelos scripts em
`tools/icons` e `tools/models` e embutidos na DLL.

O DataDump (`tools/DataDump`) exporta os dados do jogo para `<jogo>\BepInEx\dumps` (F9 exporta o save
carregado). Deixe-o desligado (`~GK2DataDump`) quando for jogar.

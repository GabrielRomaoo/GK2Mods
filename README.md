# GK2Mods

Mods para **Graveyard Keeper 2** (BepInEx 5, jogo 1.011+).

| Mod | O que faz |
|---|---|
| [Rede de Armazenamento](#rede-de-armazenamento) (`src/StorageNetwork`) | Junta os baús da base num estoque compartilhado que bancadas, o Coveiro e construções usam de qualquer zona. |
| [Planejamento da Fábrica](#planejamento-da-fábrica) (`src/FactoryPlanner`) | Quadro de Planejamento: escolha os pedidos das lojas da vila e veja as máquinas, extensões e insumos que a fábrica precisa, e qual pedido sai mais barato acrescentar. Mesa de Arquitetura: copie trechos da fábrica em plantas e construa-as de novo pelo caminho normal do jogo. |
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
| **Conector** | Terminar a obra do Armazenamento Central | 4 tábuas, 4 pregos de ferro, 1 engrenagem de bronze | Coloque perto de uma bancada ou de canteiros e vinhedos, em **qualquer zona**: eles passam a usar o estoque da rede (nos canteiros, sementes e adubo). A zona inteira também pode pagar construções com a rede. E é uma **caixa de envio**: o que você guardar nele vai para o Armazenamento Central uma vez por dia. |
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
- **Plantações:** canteiros e vinhedos dentro do alcance de um Conector plantam e adubam com o estoque da rede.
  Na janela do canteiro, a escolha de semente ou adubo mostra também os baús da rede que têm algo útil, e a
  contagem de sementes inclui a rede. A colheita fica na zona, como qualquer produção.
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

## Planejamento da Fábrica

A fábrica existe para atender as encomendas da vila, mas o jogo não diz o que cada encomenda exige. O
**Quadro de Planejamento** faz essa conta: você escolhe os pedidos e ele mostra a linha de produção inteira,
o que a sua fábrica já faz e o que falta. Ele só planeja. Construir, ligar as esteiras e balancear é com você.

Versão 0.3 (em teste). Instalação igual à da Rede de Armazenamento, com a pasta `GK2FactoryPlanner`.

### O Quadro de Planejamento

| Como liberar | Custo padrão | Onde |
|---|---|---|
| Tecnologia *Caixote* (árvore de Construção). Saves que já têm a tecnologia ganham o Quadro ao carregar. | 6 placas de madeira, 6 pregos de ferro, 4 papéis em branco, 2 engrenagens de bronze | Nos mesmos lugares de um Baú Simples. No mundo é um quadro de giz num cavalete, com o cronograma da semana e pedidos presos com barbante. |

Ao interagir, abre a janela própria do Quadro (moldura e botões do jogo), com duas colunas:

- **Pedidos** (esquerda):
  - **Na planta**: os pedidos escolhidos e a felicidade por semana.
  - **Sugeridos**: os pedidos que pedem menos máquinas novas, aproveitando o que a fábrica e a planta já fazem. É
    o caminho para crescer a fábrica aos poucos.
  - Depois, os aceitos no quadro de pedidos e os de cada loja, com o nível atual dela. Pedidos em cinza ainda não
    podem ser feitos (nível da loja ou receita não pesquisada); a dica diz o que falta.
  - Clique põe ou tira da planta. A planta fica salva no save.
- **Produção da semana** (direita), por etapa (Refino, Montagem, Produto final). Cada receita mostra:
  - a quantidade da semana;
  - que máquina vai fazê-la: uma que já faz, uma que vai trocar de receita ou "sem máquina livre";
  - **+ e −** para mudar a quantidade (um craft por clique; com Shift, 5). Os insumos acompanham: mais caixas pedem
    mais kits, e uma receita em 0 sai da semana. A quantidade mudada fica azul; **auto** volta à dos pedidos.
  - **fora** (nos produtos intermediários): traz pronto de fora em vez de fazer; **fazer** desfaz na lista de
    matéria-prima.
  - No fim, a **matéria-prima** da semana contra o que as reservas e os baús da fábrica têm agora. Ela já está
    dentro da fábrica: os zumbis enchem as reservas e as máquinas puxam pela esteira.
  - Embaixo do cabeçalho de cada etapa, o que ela **recebe** e de onde (de uma etapa anterior, da reserva ou de fora).

**Cadeia de uma peça:** clique numa receita para ver:

- o que ela precisa por semana e de onde cada insumo sai hoje: quantas máquinas o fazem, quanto há em baús e
  reservas, ou "nenhuma máquina faz hoje". Clique num insumo para descer na cadeia dele; **< Voltar** sobe;
- para onde ela vai: as receitas que a usam ou o pedido da semana;
- um **mapa da fábrica**, do jeito que a câmera vê. Cada insumo tem uma cor; verde marca quem já faz a peça, azul
  quem a usa, branco é o Quadro. O retângulo **dourado** é o melhor chão livre para mais uma máquina: o que pede
  menos esteira até os insumos e até quem usa a peça. A nota embaixo diz quantas esteiras, mais ou menos;
- **Mostrar na fábrica**: fecha o Quadro e pinta essas peças no mundo por 45 segundos;
- **Montar o módulo**: abre a Planta com o módulo da peça, com as fontes pintadas por um minuto.

**Disparar** põe as máquinas que já existem para produzir: **Disparar etapa** (no cabeçalho de cada etapa) ou
**Disparar a semana** (no rodapé). O primeiro clique mostra no rodapé o que vai mudar; o segundo confirma.

- Máquinas que já fazem uma receita da semana continuam nela, só com a quantidade nova.
- As outras receitas vão para uma máquina parada que possa rodá-las (com a extensão certa) ou, se não houver, para
  uma cuja receita a semana não usa.
- Se ainda assim faltar máquina, a receita entra na **fila** de uma máquina já em uso que possa rodá-la e receba os
  insumos: ela faz uma receita depois da outra, e a linha mostra "pôr na fila, depois de X". A bancada só puxa o
  insumo da receita da vez, então insumos das duas pela mesma esteira pedem o lado Sob demanda no baú.
- Cada máquina recebe a sua parte da quantidade e para quando termina.
- Uma troca que espera o craft terminar fica guardada no save: salvar e sair logo depois do disparo não a perde.
- Uma máquina no meio de um craft termina esse antes de trocar, como no jogo; nada se perde.

A janela de baú antiga continua disponível pela configuração `3 Planner / ClassicWindow`, e o Quadro volta para
ela sozinho se a janela própria não abrir (o log diz por quê).

### Mapa da fábrica (em teste)

O botão **Mapa da fábrica**, no canto superior direito do Quadro, troca a visão da semana pelo chão da fábrica
como ele está agora (**< Semana** volta). Abra o Quadro dentro da fábrica, para as peças estarem carregadas.

- **Cores das máquinas**: verde produzindo, amarelo esperando insumo, vermelho com a saída travada, cinza sem
  receita, roxo marcada para demolição. Baús e reservas em marrom; esteira com item mais clara que a vazia.
- **Visão geral** (esquerda): quantas máquinas em cada estado, a energia e a lista das **paradas**, cada uma com o
  motivo ("nenhuma esteira traz X", "a saída termina num Baú de Esteira, que não aceita caixa"...).
- **Clique numa peça** para ver o cartão dela:
  - máquina: a fila de receitas, cada insumo com quanto já tem e se chega pela esteira, o que chega e a receita
    não usa, para onde o produto sai e se há zumbi no posto;
  - baú ou reserva: o que guarda e como está cada lado;
  - esteira: o item que leva.
- **Mover**: clique em Mover e depois num chão livre do mapa. É a ferramenta Mover do jogo: sem custo, e a peça
  leva receita, estoque, extensões e zumbi. As esteiras só se ligam de novo se encostarem no lugar novo. Lugar
  ocupado ou fora da fábrica é recusado com aviso. Um lado de baú configurado volta quando a mesma esteira religa.
- **Remover** (dois cliques): é a ferramenta Remover do jogo. Esteira e baú saem na hora e largam no chão o que
  custaram e o que guardavam; máquina fica marcada para demolição e precisa de trabalho. Clicar de novo numa
  máquina marcada cancela.
- **Atualizar o mapa** relê a fábrica; **Mostrar na fábrica** fecha o Quadro e pinta a peça no mundo.

### Mesa de Arquitetura e plantas (em teste)

| Como liberar | Custo padrão | Onde |
|---|---|---|
| Aparece no menu de construção assim que existe um Quadro de Planejamento. | 8 placas de madeira, 6 pregos de ferro, 6 papéis em branco, 2 engrenagens de bronze | Nos mesmos lugares de um Baú Simples e dentro da fábrica. No mundo é um cavalete de arquiteto com uma planta azul presa, régua T, esquadro, compasso e lápis. |

Ao interagir, abre a janela de baú do jogo com duas colunas:

- **Plantas** (esquerda):
  - **Decalque**: fecha a janela e abre o modo de construção da fábrica. Clique nas peças para marcar (ficam
    amarelas; clique de novo desmarca). Clique no chão vazio e depois noutro ponto para marcar um retângulo inteiro.
    **R** salva as peças marcadas como uma planta; as extensões das máquinas marcadas entram na lista "à mão".
  - **Recomendadas · <família>**: as linhas da sua própria fábrica que já funcionam, separadas sozinhas. Uma linha
    é um grupo de máquinas ligadas por esteiras, com os baús das pontas. A família é a matéria-prima principal que
    a linha consome. Clique põe uma cópia da linha no chão; clique direito salva a linha em Minhas plantas.
  - **Minhas plantas**: um clique põe a planta no chão. As marcadas com "(semana)" fazem algo do cronograma da semana. A
    dica mostra peças, o que a planta faz e o custo total contra o que você e os baús têm. Clique direito duas
    vezes apaga a planta: o arquivo vai para a pasta `lixeira`.
- **Cronograma da semana** (direita): os pedidos escolhidos no Quadro e as **máquinas novas** que eles pedem, com
  quantas já têm planta e quais linhas da fábrica são da mesma família. Clicar numa máquina com planta põe essa
  planta no chão. As plantas ficam em
  `BepInEx\config\GK2FactoryPlanner\blueprints` e servem para todos os saves.

Com a planta no chão: azul cabe, vermelho está em cima de algo. Clique para construir: cada peça passa pelo
caminho normal de construção do jogo, então ele confere o lugar, cobra o material (o seu e o dos baús da
fábrica), põe as máquinas como obra para você terminar e liga as esteiras. O que não couber ou faltar material
fica desenhado; **R** prende o desenho na última obra, e clicar de novo completa só o que falta. **Esc** volta
para o jogo.

O Quadro também avisa, na dica de cada máquina, se alguma planta da Mesa já a faz.

#### Módulos por etapa

A coluna da direita abre em **Módulos por etapa**, independente dos pedidos da semana. São todos os produtos da
fábrica, separados por etapa e pela família da saída (Ferro, Madeira...):

- **Etapa 1 · Refino**: o que sai direto da matéria-prima (lingote, kits de madeira e pedra, vidro...).
- **Etapa 2, 3 · Montagem**: o que pega do estoque das etapas anteriores (Kit de Ferro, Caixote...).
- **Produto final**: o que nada mais consome (as caixas), saindo numa estação.

A etapa de uma receita é uma a mais que a do insumo mais refinado que ela usa.

Cada produto tem um **módulo**: a máquina, um Baú de Esteira atrás de cada entrada e outro na saída (o estoque
da etapa). Um baú guarda um item só, sem filtro. Clique põe o módulo no chão; clique direito salva em Minhas
plantas.

Para **encadear**, ponha o módulo seguinte com o baú de entrada em cima do baú de saída do anterior: a peça que
já está lá é aproveitada. Os baús de matéria-prima são enchidos à mão.

Fornalha I e Bancadas I/II têm a forma conferida na fábrica. Outras máquinas têm a forma lida de uma igual já
construída; se não houver nenhuma, a forma é presumida igual à da bancada, e a dica avisa.

"Ver o cronograma da semana" (em Ferramentas) troca a coluna para os pedidos do Quadro e as máquinas novas que
eles pedem.

Limites desta versão:

- A planta não gira: ela sai na mesma orientação em que foi copiada.
- Extensões (martelo, prensa…) não entram: a planta lista quais pôr à mão depois que a máquina ficar pronta.
- As paletes da fábrica não têm item no menu de construção, então ficam de fora.

### Lados do Baú de Esteira (em teste)

No jogo, cada lado de saída do Baú de Esteira aceita um item só, ou "?" (sai o primeiro item do baú). Com o mod,
na mesma janela do baú, cada lado de saída pode ser:

- **Lista de itens**: clique no lado e escolha um item do baú. Repita para somar outros itens ao mesmo lado
  (o "+n" na célula mostra quantos a mais); escolher um item que já está na lista tira esse item. Os itens da
  lista saem em rodízio.
- **Qualquer** ("?"): como no jogo.
- **Sob demanda** (célula verde): o baú olha a máquina no fim da esteira daquele lado e manda só o que a
  receita atual dela ainda precisa, um craft por vez, contando o que já está a caminho na esteira. Assim um
  baú com vários itens misturados abastece a bancada por uma esteira só, sem um item encher a esteira e travar
  os outros. Um item encalhado num ramo onde nenhuma máquina o usa não conta como "a caminho". Outro baú no
  caminho encerra a busca.
  - **Divisor por demanda**: um divisor depois de um lado Sob demanda deixa de alternar às cegas e entrega o item
    à saída que tem uso para ele, nesta ordem: máquina que ainda precisa; saída que termina em baú ou estação;
    máquina que usa mas já está abastecida; por último, saída onde ninguém usa. Em empate ele alterna como no
    jogo. Com isso um baú abastece, por um divisor, máquinas em receitas diferentes. Os outros divisores da
    fábrica não mudam. Desliga em `7 Splitter / ByDemand`.
- **O resto** (célula dourada): sai tudo o que os outros lados do baú não listam.
- **Bloqueado** (célula vermelha, sem seta): nada sai por ali.

Botão direito num lado com lista limpa a lista. Num lado sem lista, alterna Qualquer > Sob demanda > O resto > Bloqueado.
A dica ao passar o mouse diz o que o lado está fazendo.

A regra de um lado some quando a esteira daquele lado é retirada, como o filtro do jogo. O primeiro item da
lista fica no filtro do próprio jogo: sem o mod, o lado continua filtrado por ele, e "O resto" e "Bloqueado"
voltam a ser "?". O disparo da semana lê essas regras para saber o que chega em cada máquina; o que vem por um lado sob demanda não conta como item sobrando na esteira.

### Suprimentos de fora (em teste)

O jogo só leva sete matérias-primas para dentro da fábrica (madeira, pedra, mármore, argila, areia, carvão e
minério), além do depósito da horta e do palete de vinho e cerveja. O que mais uma receita pedir (Madeira
Especial, Linho, Lingote de aço, Poder Zumbi...) tinha de ser posto à mão num Baú de Esteira.

Depois que você dispara a semana inteira no Quadro, os **Zumbis Entregadores de Suprimentos** da fábrica (os
mesmos que levam as caixas para fora) passam a trazer esses itens do estoque da **Rede de Armazenamento**:

- **Quando**: no disparo e depois uma vez a cada novo dia.
- **O quê**: só o que nenhuma receita da fábrica produz e que a fábrica não tem em reserva.
- **Para onde**: um Baú de Esteira nas esteiras de entrada da máquina que usa o item. Primeiro um que já guarda
  esse item, depois um sem esteira de entrada (o abastecido à mão), depois o mais próximo.
- **Quanto**: completa o baú até o consumo de um dia (a conta da semana dividida por 6).
- **Limite**: 120 itens por dia para cada Zumbi Entregador trabalhando na fábrica.

Sem Zumbi Entregador, sem Armazenamento Central ou sem o item na rede, nada se move, e a mensagem do disparo
diz o que faltou. Um Baú de Esteira sem esteira de entrada conta, para o disparo, como capaz de fornecer o que
a rede tem em estoque. Configuração: seção `6 Supply` (`Enabled`, `ItemsPerZombie`).

### Próximas versões (planejado)

- **Obras por zumbis (0.4)**: mandar construir pelo mapa, pago com o estoque de suprimentos. Uma estação empacota
  o material da obra num palete e um zumbi de profissão nova, o **Construtor**, leva e ergue. Mover e remover
  passam a seguir o mesmo fluxo, com preço, e o material de uma demolição volta ao estoque.
- **Modelos prontos** de plantas por família (Ferro, Madeira, Pedra…), tirados de trechos que funcionam.
- **Mesa do Capataz**: bancada onde você "fabrica" lotes da semana. Ela consome os insumos na hora e gera
  Caixas de Insumos.
- **Calendário no Quadro**: o próprio Quadro ganha o calendário da semana, ligado aos pedidos aceitos.
- **Luneta do Supervisor**:
  - produção real contra o plano;
  - máquinas paradas e gargalos;
  - linhas certificadas que viram módulos reaproveitáveis.

### Configuração

`BepInEx\config\gk2mods.factoryplanner.cfg`: tecnologia de desbloqueio, custos do Quadro e da Mesa, janela clássica do Quadro, quantas
sugestões mostrar, suprimentos de fora, divisor por demanda, log e modo de teste (Quadro e Mesa sem tecnologia,
por 1 placa de madeira).

Testes sem o jogo: `dotnet run --project tests/FactoryPlanner.Tests` roda as checagens que não precisam de dados
do jogo (fila da bancada, escolha de saída do divisor). Com `recipes.json orders.txt` exportados do jogo, roda
também as do planejamento.

---

## Para desenvolvedores

Requisitos: .NET SDK (qualquer versão recente, o alvo é `net472`) e o jogo instalado. Se o jogo não estiver em
`C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2`, passe `-p:GameDir="<caminho>"`.

```
cd src/StorageNetwork      (ou src/FactoryPlanner)
dotnet build -c Release
```

A lógica do planejador (`src/FactoryPlanner/Core`) não usa tipos do jogo. `tests/FactoryPlanner.Tests` roda essa
lógica fora do jogo, com receitas e pedidos extraídos dos arquivos do jogo:
`dotnet run -- recipes.json orders.txt [fabrica.txt]` (formatos no topo de `Program.cs`).

O build já copia o mod para `...\Mods\Code\GK2StorageNetwork\` (com `modinfo.json`), e o jogo instala na próxima
abertura (contrato em `<jogo>\README_MODDING.txt`). Os ícones e modelos 3D são gerados pelos scripts em
`tools/icons` e `tools/models` e embutidos na DLL.

O DataDump (`tools/DataDump`) exporta os dados do jogo para `<jogo>\BepInEx\dumps` (F9 exporta o save
carregado). Deixe-o desligado (`~GK2DataDump`) quando for jogar.

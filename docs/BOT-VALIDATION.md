# Validação de bots — ferramenta de editor

A primeira implementação mantém as regras, os pesos de `BotProfile.Normal`, a interface e os formatos dos saves. Executa partidas isoladas no núcleo com o conteúdo real, sem criar GameObjects por ação. A análise estatística de equilíbrio e a validação num telemóvel real são etapas distintas.

## Utilização

No Unity, fora de PlayMode, abrir **Tactics → Bots → Validation Batch**.

1. Selecionar **12-game smoke configuration** para uma execução curta: 2, 3 e 4 participantes × quatro combinações de Build Mode/Battle Royale. Os mapas Open, Forest, Asymmetric e Generated aparecem nesta amostra, mas mapa e modo estão associados; esta amostra não mede equilíbrio.
2. Editar o JSON ou carregar uma configuração guardada. A lista `scenarios` é explícita e a ordem de `races` determina os lugares P1–P4 e a ordem inicial dos turnos. Usar IDs do catálogo, como `race.human`, `race.orc` e `race.elf`.
3. Configurar `limits.maxRounds`, `limits.maxActions` e `limits.maxPlanningMilliseconds`. São limites técnicos de segurança. O último mede CPU de planeamento por decisão, excluindo o intervalo entre ticks do editor.
4. Escolher `outputDirectory`. Por omissão, os resultados ficam em `BotValidationResults`, ignorado pelo Git. Cada execução cria uma subpasta nova; não escreve nos mapas, saves ou preferências do jogador. Destinos dentro de Assets ou de `persistentDataPath` são rejeitados.
5. Carregar em **Start batch**. A execução avança em ticks com um orçamento de cerca de 8 ms; um passo indivisível do planeador pode ultrapassar esse orçamento. **Cancel and preserve diagnostics** conserva partidas terminadas e exporta a partida parcial. Recompilar scripts, fechar o editor ou entrar em PlayMode também cancela o lote.
6. Para repetir uma partida, usar **Replay match directory** e selecionar a pasta `match-NNNN`. O replay usa o estado inicial, o conteúdo e o perfil exportados, verificando os hashes das entradas. Numa partida cancelada, repete apenas o prefixo de ações executadas. `replay-verification.json` compara o hash final com o original. Uma mudança no código do planeador pode produzir uma diferença; não se trata de uma promessa de equivalência entre versões do código.

**Full reference matrix** prepara 576 cenários: 2–4 participantes, quatro modos, os mapas Symmetric/Forest/Asymmetric/Generated, três raças iguais ou três rotações mistas e duas seeds de sorte. Medir primeiro uma amostra curta; a matriz completa pode demorar bastante e não inclui automaticamente mapas personalizados do jogador.

## Configuração dos cenários

- `map`: `Open`, `Symmetric`, `Forest`, `Asymmetric`, `Generated` ou `Custom`. Symmetric preserva terreno e geometria dos castelos sob rotação de 180 graus. Open conserva a fixture inicial do lote curto, incluindo o castelo central isolado; não constitui uma referência perfeitamente simétrica. Nos três participantes, os lugares mantêm os três cantos usados pelo jogo, com acesso diferente ao centro mesmo quando o terreno é simétrico.
- `width`/`height`: 10–50 para os mapas de referência. Um mapa Custom usa as suas próprias dimensões e inícios, validando o número de participantes e os caminhos com `CustomMapValidator`.
- `terrainSeed`: controla a geração de terreno, usando o mesmo algoritmo e a mesma ordem de sorteios de `Map.GenerateMap`. O comportamento normal do jogo continua sem seed explícita; existe um overload para uso reproduzível.
- `luckSeed`: estado inicial da sequência de combate. A sequência fica nos diagnósticos; `BotObservation` continua a apagá-la antes do planeamento.
- `startingMoney`: 1000 por omissão, correspondente às equipas atuais da Main Scene; Build Mode começa com os 4000 existentes. `MatchEngine.BeginTurn` aplica rendimento/cura antes da primeira decisão. O dinheiro pode ser configurado como cenário experimental, sem modificar o jogo.
- `assignStartingCastles`: acrescenta castelos próprios nos inícios. Representa uma configuração de mapa, permitida pelas regras atuais. O resumo distingue terrenos, inícios e donos através de `mapIdentity`.
- `customMapPath`: ficheiro JSON de mapa existente, lido sem o alterar. Não é necessário selecionar esse mapa na partida apresentada.
- `useInitialState`: quando true, `initial` deve conter um `SimulationSnapshot` completo, já no início de uma decisão. Não se volta a aplicar `BeginTurn`. Esta opção permite comparar precisamente estados exportados, incluindo tropas, economia e tempestade. O booleano é necessário porque o JsonUtility pode materializar classes nulas como objetos vazios.

O campo `profile` configura exclusivamente pesos estratégicos. A ferramenta nunca concede dinheiro, visão, vida ou dano adicional ao bot. Mudanças de regras ou estatísticas de conteúdo continuam a exigir aprovação, como definido no handoff.

## Resultados e reprodução

Cada execução contém:

- `manifest.json`: configuração, versão Unity e hashes SHA-256 do conteúdo, perfil e implementação do núcleo/geração.
- `configuration.json`: configuração isolada que pode ser aberta por **Load JSON** e usada numa nova execução.
- `content.json`: unidades, recrutamento e tabela de dano extraídos pela mesma função usada por `UnityMatchAdapter`. Permite repetir a partida mesmo quando os assets mudam.
- `summary.json`: estado do lote, partidas com resultado exportado, vitórias, empates, limites, falhas, cancelamentos e grupos por identidade de mapa/modo/participantes/raça/lugar/ordem. `finished` conta resultados exportados, incluindo inconclusivos; apenas `victories + draws` são partidas concluídas normalmente.
- `match-NNNN/initial.json`: estado inicial completo, materializado antes de executar o lote.
- `match-NNNN/decisions.jsonl`: ação, ator, alvos, score, razão, candidatos, tempo de CPU de planeamento e hash anterior de cada decisão. O stream é fechado e conservado ao cancelar.
- `match-NNNN/result.json`: resultado e motivo, rondas/turnos/ações, contagens de ações, derrotas de líderes/unidades, dano/mortes diretamente causados pela tempestade, tempos, amostras de dinheiro/unidades/castelos por turno e as últimas 32 decisões.
- `match-NNNN/final.json`: estado final ou parcial, incluindo a sequência de sorte restante.

Enums nos JSON são numéricos. `SimulationOutcome`: 0 Running, 1 Victory, 2 Draw, 3 LimitReached, 4 Cancelled, 5 RejectedAction, 6 NullDecision, 7 Exception. `MatchActionKind`: 0 Wait, 1 Attack, 2 Capture, 3 BuildCastle, 4 Recruit, 5 EndTurn.

`LimitReached` é inconclusivo, nunca empate. `repeatedBoards` identifica repetições por turno ignorando dinheiro e o avanço da sorte/turno: é um sinal de diagnóstico, não uma regra de derrota nem uma definição suficiente de bloqueio. As amostras de economia e o trace permitem distinguir repetição produtiva de passividade. `unitLosses` inclui a eliminação das tropas após perder o líder; `stormDeaths` conta apenas mortes pelo dano direto da tempestade.

O lugar e a ordem do turno são correlacionados nos inícios normais. A contagem de `appearances` é por participante naquele contexto, não uma estimativa global de vantagem racial. Comparações de estratégia devem conservar os estados iniciais, o conteúdo, as seeds e os limites; mudar apenas o perfil ou o código em avaliação e conservar ambos os lotes.

## Limitações desta etapa

A velocidade do simulador não mede a fluidez das animações ou dos bots na build. A amostra curta não sustenta alterações de equilíbrio. Limites e repetições precisam de diagnóstico com as mesmas entradas antes de alterar os pesos. A duração desejada, a agressividade do bot e quaisquer alterações de conteúdo permanecem decisões de design do utilizador. O simulador não substitui uma partida completa num telemóvel real.

## Preço individual de cada tropa

A janela inclui **Individual troop prices** e **Individual prices: equal armies**. Os dois presets abrangem as 13 tropas recrutáveis atuais. Cada tropa é avaliada individualmente ao custo atual, −10% e +10%, contra as outras duas raças e nos dois lugares. Há 156 cenários por preset, com mapa Symmetric e seed 12345. Os preços são inteiros arredondados; estes valores são tratamentos experimentais, não preços aprovados.

O preset de recrutamento restringe a raça em teste à tropa avaliada, para observar essa unidade sem que o bot escolha sempre uma alternativa. O adversário mantém o catálogo completo. A restrição é identificada por `priceFocusRecruitment`; pode ser false num ensaio adicional com o catálogo completo. Todos os bots mantêm o perfil normal e os mesmos rendimentos/regras. O preço experimental é aplicado globalmente ao tipo de unidade dentro daquela cópia de conteúdo, nunca como vantagem exclusiva de um jogador.

O preset de exércitos começa com um orçamento adicional de 3000 por equipa, convertido em tropas: a unidade em teste contra a tropa recrutável mais barata da raça adversária. A quantidade é `floor(orçamento/preço)`; dinheiro não gasto fica na equipa. Cada lado mantém o dinheiro/rendimento inicial normal. As tropas iniciais são tropas já presentes, não recém-recrutadas, e podem agir no seu turno. É um cenário controlado para comparar composição pelo mesmo orçamento. Depois do início, a economia e o recrutamento seguem as regras normais; este preset não representa automaticamente todas as composições possíveis.

No ensaio de recrutamento, os estados iniciais completos são idênticos entre os três preços de cada par. No ensaio de orçamento, a composição e o dinheiro restante variam precisamente porque o preço varia. Os pares mantêm mapa, oponente, lugares, seed e orçamento. O campo `pricePairId` identifica estes pares.

Para personalizar a configuração, editar os campos `priceUnitId`, `priceCost`, `priceBaselineCost`, `priceRaceId`, `pricePlayer`, `pricePairId`, `priceFocusRecruitment` e `priceArmyBudget` nos cenários. A API de editor `UnitPriceExperiments.Create(UnitPriceSettings)` permite escolher unidades (`unitIds`), mapas (`maps`), seeds (`luckSeeds`), multiplicadores (`multipliers`) e orçamento (`armyBudget`). O custo original é sempre incluído. Alterar o preço de uma única tropa por tratamento conserva a atribuição do efeito; outras alterações de estatísticas ou à tabela de dano não são efetuadas por esta ferramenta.

Os testes de preço acrescentam:

- `match-NNNN/case-manifest.json`: cenário e hash do conteúdo específico da partida. O resumo geral separa conteúdos diferentes.
- `match-NNNN/content.json`: cópia exata do conteúdo experimental, enquanto o `content.json` na raiz conserva o conteúdo original.
- `match-NNNN/price-result.json`: custo original/experimental, tropas iniciais, recrutadas, dinheiro gasto em recrutamento, primeira ronda de recrutamento, ataques, capturas, dano causado/recebido, mortes diretas, mortes de líderes, perdas de tropas e sobreviventes da unidade em teste.
- `price-summary.json`: resultados por tropa/preço/preset, incluindo vitórias, derrotas, empates, inconclusivos, casos sem presença da tropa e comparação emparelhada com o preço original. `pairedCompletedGames` exclui resultados por limite/cancelamento/falha. `pairedWithoutUnit` assinala pares em que a tropa não esteve presente numa das variantes; não são evidência suficiente de valor individual.
- `price-strikes-HASH.json`: previsões de um ataque e contra-ataque contra as tropas recrutáveis, com 1/2/3 estrelas e distância de contacto/alcance máximo. Usam `MatchEngine.Predict`, cobrindo a sorte de 0 a 9, sem consumir a sequência da partida. São previsões de um ataque, não duelos completos com movimento e cura.

`enemyGoldDamage` estima a fração de vida removida a tropas adversárias multiplicada pelo seu custo; líderes são contabilizados separadamente. A cura pode tornar o dano repetido improdutivo, por isso esta estimativa não determina um preço justo. Captura, movimento, alcance, sobrevivência e resultado da partida também contam. O custo também entra nas heurísticas de recrutamento e risco do bot, portanto as diferenças em partidas incluem a reação do bot ao preço. As previsões de combate ajudam a separar essa reação da força mecânica da tropa. Os relatórios não geram um preço recomendado automaticamente, nem confundem zero recrutamentos com boa relação custo/benefício.

Os assets, saves, preferências e a partida apresentada não são alterados. O replay reconstrói o tratamento a partir do conteúdo original congelado e verifica o hash da cópia experimental. Aplicar novos preços ao jogo continua a exigir uma proposta concreta e aprovação.

### Validação inicial dos ensaios de preço

O preset de recrutamento foi executado integralmente em `BotValidationResults/20261013-030353-9fcf61da`: 156 partidas, 148 vitórias e oito inconclusivos por limite. Todas as 13 tropas foram recrutadas em todas as suas 12 partidas; registaram-se 1601 recrutamentos. Foram exportados 39 grupos de tropa/preço e 39 conjuntos de previsões de combate. Não houve falhas de execução. O conteúdo real extraído dos assets após o lote ficou byte a byte igual ao conteúdo original congelado, confirmando que os preços e estatísticas do jogo não foram alterados. O replay do ensaio Soldier a 675 reproduziu exatamente o hash final.

O preset de orçamento igual teve uma execução curta em `20261013-031646-e538ce9f`: 39 partidas, três preços para cada uma das 13 tropas, 32 vitórias e sete inconclusivos, sem falhas de execução. Esta amostra usa o primeiro adversário configurado e o lugar P1; o preset completo de 156 cenários mantém disponíveis os dois adversários e a troca dos lugares. A amostra curta serve para verificar o executor e os tratamentos, não para concluir qual o preço justo de uma tropa.

Após esta extensão, passaram 187/187 testes EditMode, incluindo conservação do orçamento, isolamento das alterações de preço, invariância do combate, limiares de recrutamento, cobertura das 13 tropas e telemetria de dano/recrutamento. Evidência em `BotValidationResults/unit-price-tests.json`. A extensão altera apenas ferramentas de editor e testes; o núcleo e o código da build do jogador ficaram iguais.

## Primeira baseline — 13 de outubro de 2026

O lote curto em `BotValidationResults/20261013-015512-1b527678` concluiu 12/12 partidas, com 1573 ações, sem rejeições, decisões nulas ou exceções. Demorou 24,65 segundos no editor. O replay da primeira partida produziu exatamente o mesmo hash final.

O lote ampliado em `BotValidationResults/20261013-015850-df3fd231` exportou 150 resultados: 140 vitórias e 10 casos inconclusivos por limite de rondas. Executou 23229 ações em 345,13 segundos, sem rejeições, decisões nulas ou exceções. Das partidas concluídas, a duração variou entre 6 e 57 rondas; o máximo de CPU de planeamento observado por decisão foi 35,18 ms. Estes tempos descrevem este simulador/editor, não um telemóvel.

A amostra ampliada contém 144 partidas no mapa Open: as três raças iguais ou três rotações mistas, duas seeds de sorte (12345 e 67890), 2–4 participantes e quatro modos. Há ainda seis partidas com castelos próprios nos inícios. Por combinação, foram exportadas 12 partidas nos modos com construção e 13 nos modos sem construção. As 12 partidas do lote curto acrescentam cobertura de floresta, assimetria e terreno gerado; não se executou ainda a matriz de 576 partidas nem uma amostra estatística de mapas personalizados do jogador.

Os dez limites ocorreram em oito partidas de dois participantes, uma de três e uma de quatro. Há casos de passividade: o tabuleiro repete-se enquanto a economia aumenta e os bots apenas terminam turnos. Por exemplo, `match-0000` do lote ampliado regista 166 repetições e dinheiro final de 94000/135750. Nos dois casos Battle Royale sem construção, há outro ciclo: o líder Elf ataca à distância e o Human recupera a vida num castelo no centro seguro, antes de voltar a terminar o turno (`match-0034` e `match-0035`). A repetição não implica ausência de combate. Isto demonstra impasses em certas entradas, não ilegalidade nem prova de vantagem racial. Os estados, traces e últimas decisões estão conservados para comparar ajustes futuros. Nenhum peso do bot normal, custo, estatística ou regra foi alterado nesta etapa.

O cancelamento foi exercitado em `20261013-015655-6dbe81e3`: uma vitória e uma partida parcial com 271 ações ficaram exportadas, com todas as linhas do trace e os estados finais. O replay desse prefixo em `20261013-020737-f372223c` produziu o mesmo hash final. Os logs gerados permanecem fora do versionamento.

Um mapa personalizado de referência, com floresta, montanha, castelo neutro e castelos atribuídos, foi importado sem ser selecionado na partida aberta. As quatro combinações de modos concluíram com vitória, sem falhas, em `20261013-021356-b06eb574`. A fixture está em `BotValidationResults/custom-reference.json`; é material de QA separado dos mapas do jogador.

Na revisão final foi acrescentado o mapa Symmetric, cuja geometria de terreno/castelos é verificada por testes de rotação para 2–4 participantes. As 12 combinações de participantes e modos concluíram com vitória, sem falhas, em `20261013-022942-7911ed5f`, mantendo o mesmo perfil normal. Os resultados Open anteriores foram preservados. As amostras executadas totalizam 178 cenários: 168 concluídos e dez inconclusivos; replays e ensaios de cancelamento ficam separados deste total.

Validação automatizada: baseline inicial de 160 testes EditMode; após a implementação e a adição da referência simétrica, 182/182 EditMode e 108/108 PlayMode passaram. Os resultados completos estão em `BotValidationResults/editmode-tests.json` e `playmode-tests.json`. A primeira tentativa de baseline PlayMode foi interrompida pela recompilação durante a execução do test runner, sem resultado completo; não foi tratada como sucesso nem como falha de regras preexistente. A execução final foi repetida sem alterações de scripts durante os testes.

A build WebGL de validação foi concluída com sucesso em `Build/BotValidation-WebGL-20261013`, usando o perfil Web - Desktop - Release. Não foi publicada. O relatório regista zero erros e 346 avisos: 345 de shaders do pacote de inferência e um CS0109 em `UIManager.cs`, ficheiro não alterado nesta tarefa. O resumo está em `BotValidationResults/webgl-build.json`. A Main Menu foi reposta no editor e o mute de áudio voltou ao estado anterior. A compilação Android e a partida num telemóvel físico continuam pendentes; não há módulo Android nem dispositivo disponibilizado nesta sessão.

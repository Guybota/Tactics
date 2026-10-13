# Handoff — validação e equilíbrio de partidas com bots

Preparado em 13 de outubro de 2026. Projeto: `C:\Users\andre\Tactics`. Unity: **6000.3.9f1**.

Este documento passa a próxima fase do desenvolvimento a outra pessoa ou agente. É um plano de execução; as simulações e o equilíbrio descritos abaixo ainda não foram realizados nesta fase.

## Objetivo

Validar partidas completas com os bots atuais, identificar bloqueios e vantagens sistemáticas de raças/posições iniciais e melhorar as decisões dos bots com resultados reproduzíveis. Terminar com validação de uma partida completa num telemóvel real, quando houver acesso a um dispositivo.

Primeiro produzir uma baseline e diagnósticos; depois corrigir problemas demonstrados. Perguntar ao utilizador sobre decisões de design por definir, enquanto se avança no trabalho que não depende dessas respostas.

## Estado atual

- Combate, ações, turnos e tempestade estão separados em `Tactics.Core`, sem dependências Unity.
- O bot normal usa Utility AI, com pontuação em `BotProfile`, observações isoladas e planeamento incremental. `BotRunner` executa as ações pelo mesmo caminho validado dos humanos.
- Existem seleção humano/bot para 2–4 participantes, raças atuais, Build Mode, Battle Royale, saves, editor de mapas e gestão de mapas personalizados.
- Já existem testes de partidas completas: nove combinações no núcleo com conteúdo sintético e um teste de quatro participantes com as raças reais e mapa gerado. Estes testes não constituem uma análise estatística de equilíbrio.
- A build WebGL foi publicada por butler em `guybota/lords-of-conflict:web`, versão 2, build `2116176`: https://guybota.itch.io/lords-of-conflict . O upload existente é `19723147`.
- A última correção colocou o percurso pixel art em Sorting Order **30**, por cima das unidades, e permitiu hover com rato também quando existe suporte a toque. O percurso foi observado na build publicada.
- Os nove testes focados dessa correção passaram. Na etapa anterior, 33 testes de regressão de partida/cena passaram. São resultados anteriores, não confirmação da execução atual nem de toda a suite.
- Há muitas alterações locais acumuladas. Inspecionar o estado Git e preservar trabalho existente; não fazer reset, restore geral, limpeza de assets ou reconstrução de cenas para começar esta tarefa.
- Não foi encontrado um ficheiro formal de roadmap; a fase aqui descrita foi proposta com base no estado do projeto e nas decisões do utilizador.

## Regras e limites

Ler `CLAUDE.md` na raiz antes de alterar o projeto; é a referência de decisões e restrições. As decisões explícitas do utilizador têm prioridade.

- Referência principal: Wargroove, complementada por Advance Wars 2 e Fire Emblem. Não importar outras regras desses jogos automaticamente.
- Manter o bot normal sem vantagens artificiais de dinheiro, visão, vida ou dano. O bot não pode consultar a sequência de sorte para antecipar resultados.
- Preservar a fórmula AW2, sorte de 0 a 9, previsão em intervalo e defesa de relva/floresta/castelo de 1/2/3 estrelas. Manter ações e alcance do Tactics.
- Manter captura imediata, passagem por aliados, recrutamento, construção pelo líder, economia e cura definidos em `CLAUDE.md`. Tempestade igual para todos, com mortes resolvidas em conjunto e possibilidade de empate.
- Preservar tropas, raças, modos, compatibilidade dos saves e IDs estáveis. Não mudar formatos de dados apenas para facilitar o simulador.
- Não alterar posições, layouts ou proporções da interface. Não introduzir `PanelScaleToFit` nem adaptar UI através de Transform Scale.
- Esta fase não inclui multiplayer, habilidades de líderes, críticos próprios das unidades ou novas raças/tropas.
- Corrigir bugs demonstráveis e melhorar implementação/diagnóstico dentro das regras existentes. Alterações a custos, estatísticas, danos ou regras de equilíbrio exigem uma proposta concreta e confirmação do utilizador antes de aplicar.
- Ajustes aos pesos estratégicos do bot normal podem ser feitos com comparação antes/depois, mantendo as regras e a ausência de vantagens artificiais. Perguntar se a mudança representar uma nova filosofia de dificuldade ou comportamento ainda não definida.

## Ficheiros relevantes

Os caminhos seguintes são relativos à raiz do projeto.

- `Assets/Scripts/Core/MatchEngine.cs`, `MatchState.cs`, `MatchAction.cs`, `MatchContent.cs`: estado e execução das partidas.
- `Assets/Scripts/Core/BotPlanner.cs`, `BotPlanningSession.cs`, `BotAnalysis.cs`, `BotObservation.cs`, `BotProfile.cs`: decisões, diagnóstico de ameaças e pesos do bot.
- `Assets/Scripts/BotRunner.cs`: planeamento por frames, execução e animação na partida apresentada.
- `Assets/Scripts/UnityMatchAdapter.cs`: constrói conteúdo a partir das raças/prefabs reais, captura o estado e expõe o motor. Reutilizar esta fonte de conteúdo; evitar uma segunda tabela manual de estatísticas para as medições.
- `Assets/Scripts/scr_game_contoller.cs`: integração da partida, `CreateBotPlanner`, ações confirmadas e configuração dos modos.
- `Assets/Scripts/class_map.cs`: `GenerateMap` e integração de terreno/movimento.
- `Assets/Scripts/RaceCatalog.cs`, `RaceDefinition.cs`, `UnitCombatDefinition.cs`, `CombatDamageTable.cs`: definições de conteúdo.
- `Assets/Data/RaceCatalog.asset`, `Assets/Data/Races/`, `Assets/Data/CombatDamageTable.asset`: assets de conteúdo atuais.
- `Assets/Scripts/Core/CustomMapData.cs`, `Assets/Scripts/CustomMapRepository.cs`, `Assets/Scripts/CustomMapTiles.cs`: mapas personalizados, validação e aplicação.
- `Assets/Tests/EditMode/BotPlannerTests.cs`: `AutomatedGamesRemainLegalAndReachAnOutcome`, testes de legalidade, isolamento, captura, recrutamento, construção, ameaças, tempestade e cancelamento.
- `Assets/Tests/PlayMode/BotIntegrationTests.cs`: `RealRacesAndGeneratedMapCompleteAnIsolatedFourPlayerBotMatch` e testes de pausa, saves e controlo humano/bot.
- `Assets/Tests/EditMode/MatchEngineTests.cs`, `AdvanceWarsCombatTests.cs`, `ContentCatalogTests.cs`, `CustomMapTests.cs`: regressões das regras e do conteúdo.
- `Assets/Tests/PlayMode/MatchRegressionTests.cs`, `MatchActionIntegrationTests.cs`, `MobileControlsTests.cs`, `ResponsiveUiRegressionTests.cs`: regressões da apresentação, input e ações.
- Assemblies de testes: **`Tactics.Tests`** em EditMode e **`Tactics.PlayMode.Tests`** em PlayMode.

## Plano de execução

### 1. Estabelecer uma baseline reproduzível

Inspecionar as implementações e correr os testes relevantes antes de alterar a estratégia. Registar falhas preexistentes separadamente das introduzidas pela tarefa.

**Atenção à geração aleatória:** `Map.GenerateMap` usa `new System.Random()` sem seed explícita. O teste de integração inicializa `UnityEngine.Random`, mas isso não fixa o terreno gerado por `System.Random`. Não assumir que esse teste reproduz o mesmo mapa.

Para cada cenário, guardar o estado inicial completo do terreno, inícios, castelos, participantes, unidades, dinheiro e sorte da partida. Uma seed isolada não chega enquanto todas as fontes de aleatoriedade não estiverem controladas. Se for necessário acrescentar geração com seed, preservar o comportamento normal e usar o mesmo algoritmo; evitar um gerador paralelo exclusivo das medições.

### 2. Criar um executor de simulações em lote

Preferir uma ferramenta de editor explícita, configurável, cancelável e separada da interface do jogo. Executar no núcleo, sem animações nem GameObjects por ação. Se for preciso extrair a construção de conteúdo do adapter, fazê-lo numa função partilhada, mantendo a integração existente.

Requisitos:

- Configurar lista de cenários, seeds de sorte, raças, ordem dos participantes, modos, limites e destino dos resultados.
- Reutilizar `BotPlanner`/`BotObservation` e `MatchEngine`; não criar um caminho alternativo que ignore validações.
- Executar sobre cópias isoladas; não alterar a partida aberta, os mapas do utilizador, os saves ou as preferências.
- Repartir o trabalho para manter o editor responsivo; permitir cancelar e conservar os resultados já terminados.
- Exportar resultados por partida e um resumo agregado, com configuração e identificação do conteúdo/perfil usados.
- Guardar informação suficiente para reproduzir cada falha ou partida suspeita.

### 3. Cobrir os cenários relevantes

Começar com uma execução curta que cubra 2, 3 e 4 participantes e as quatro combinações de Build Mode/Battle Royale, incluindo Battle Royale sem construção. Os testes atuais não cobrem todas essas combinações no lote de partidas completas.

Incluir:

- Partidas com a mesma raça em todos os lugares, para detetar efeitos da posição e da ordem do turno.
- Partidas com raças diferentes e troca das raças entre posições, para reduzir a confusão entre vantagem da raça e do lugar.
- Mapas simétricos de referência, terrenos com florestas/gargalos, mapas assimétricos válidos e mapas gerados/personalizados representativos.
- Castelos neutros e atribuídos, conforme os modos e configurações atuais.
- Várias configurações de sorte sobre o mesmo estado inicial.

Expandir o lote depois de medir o custo da execução curta. Registar o número de partidas efetivamente concluídas por combinação; não apresentar uma pequena amostra como prova definitiva de equilíbrio.

### 4. Diagnosticar e corrigir com comparação antes/depois

Separar falhas de regras, bugs de execução e decisões estratégicas fracas. Corrigir primeiro ações ilegais, bloqueios e falhas de integração. Depois avaliar proteção do líder, combate, captura, recrutamento, construção e fuga da tempestade.

Para cada ajuste, conservar a baseline e repetir os mesmos cenários/estados/seeds, identificando ganhos e regressões. Acrescentar testes específicos para bugs reproduzidos; evitar testes que apenas copiem a implementação.

Apresentar alterações de equilíbrio de conteúdo como propostas fundamentadas. Se uma decisão de design estiver pendente, continuar a instrumentação, os testes e os diagnósticos independentes dessa decisão.

## Métricas e diagnóstico

Registar, pelo menos:

- Resultado: vencedor/empate, derrotas, rondas, turnos e ações executadas.
- Vitórias por raça, posição inicial e ordem do turno, acompanhadas do número de partidas e contexto do mapa/modo.
- Tempo de planeamento por decisão e duração do lote; distinguir velocidade do simulador de fluidez na build real.
- Ações por tipo, recrutamento, construção, captura e perdas de líderes/unidades.
- Dinheiro, número de unidades e castelos ao longo da partida; dano/mortes pela tempestade.
- Ações rejeitadas, decisões nulas, exceções, repetições/ciclos suspeitos e motivos de interrupção.
- Em decisões relevantes: `BotDecision.Action`, `Score` e `Reason`, contexto e estado inicial para reprodução.

Os testes existentes usam limites de **100 rondas e 5000 ações**. Podem servir de limite inicial de segurança, configurável; não são regras da partida nem metas aprovadas de duração.

Uma simulação interrompida por limite deve ser classificada como **inconclusiva/limite atingido**, nunca como empate normal. Repetição de um hash de estado é um sinal, não a única definição de bloqueio; considerar turnos, economia e progresso esperado. Guardar estado e últimas decisões para diagnosticar.

Registar a sorte inicial para reprodução é permitido; o planeador continua a receber observações sem acesso à sequência de sorte.

## Decisões por confirmar

Perguntar apenas quando os resultados ou a implementação dependerem da resposta:

- Qual é a duração pretendida de uma partida normal e de Battle Royale?
- Que nível de agressividade e proteção do líder se pretende para o bot normal, se for necessário alterar significativamente a estratégia?
- Há vantagens raciais deliberadas ou um objetivo de equilíbrio uniforme? Que alterações concretas de custos/estatísticas/danos aprova o utilizador?
- Que telemóvel e browser/build Android estarão disponíveis para validação física?

Não bloquear a baseline nem a ferramenta de simulação enquanto estas respostas estiverem em aberto. Distinguir defaults técnicos propostos de decisões já aprovadas.

## Validação em mobile

Os testes automatizados e o dimensionamento de um browser de desktop não substituem uma partida num dispositivo real.

Quando houver acesso, verificar numa partida completa:

- Landscape, toque para selecionar, arrasto para deslocar, pinça para zoom e ausência de ações involuntárias ao terminar os gestos.
- Turnos dos bots, fluidez do planeamento/animações, pausas em menus/orientação e retoma.
- Combate, captura, recrutamento, construção e tempestade nos modos pertinentes.
- Guardar/carregar e resultado final de vitória ou empate.
- Controlos e painéis nos layouts atuais, sem os redistribuir.

Registar modelo do dispositivo, sistema, browser ou versão da build e problemas reproduzíveis. Sem acesso a hardware, concluir o trabalho independente, documentar esta validação como **pendente** e pedir ao utilizador os testes necessários; não declarar a fase mobile concluída.

## Critérios de conclusão

- Ferramenta em lote funcional, configurável e cancelável, com reprodução dos cenários e resultados exportáveis.
- Cobertura documentada de 2–4 participantes, raças/posições e quatro combinações dos modos, indicando amostras e limitações.
- Nenhuma ação ilegal, exceção ou alteração involuntária da partida aberta nos cenários aceites.
- Partidas que atingem limites ou revelam ciclos classificadas e diagnosticadas; problemas restantes explicitamente identificados.
- Ajustes justificados por comparações sobre as mesmas entradas, sem vantagens artificiais nem mudanças de regras não aprovadas.
- Regressões relevantes de núcleo, bot, integração, saves, mapas e controlos passam após as alterações.
- Compilação das plataformas afetadas verificada. No Unity, manter áudio desligado durante QA e repor o estado anterior ao terminar.
- Validação física mobile concluída com evidência, ou claramente assinalada como pendente por falta de dispositivo.

## Entrega esperada

Entregar código/ferramenta, instruções de utilização, configuração do lote, resultados brutos, resumo da baseline e das comparações, casos de reprodução, testes executados e questões/limitações restantes.

Guardar relatórios e capturas num destino dedicado; não misturar resultados com mapas/saves do utilizador. Evitar versionar grandes logs gerados sem necessidade.

Se for preciso atualizar a versão publicada, gerar uma build nova e verificar o resultado antes de enviar. Destino conhecido: `guybota/lords-of-conflict:web`. Butler instalado em `C:\Users\andre\AppData\Roaming\itch\broth\butler\versions\15.24.0\butler.exe`; o executável nessa pasta reportava versão 15.31.0. Confirmar caminho/sessão atuais e nunca incluir credenciais em relatórios ou no documento. Não publicar automaticamente lotes ou alterações de equilíbrio ainda em revisão.

Usar Unity MCP quando disponível: ler primeiro o estado do editor, confirmar compilação e verificar a consola. Consultar a skill `unity-mcp-orchestrator` instalada em `C:\Users\andre\.codex\skills\unity-mcp-skill\SKILL.md` para o fluxo e os schemas atuais. Se for necessário abrir outra cena, preservar cenas sujas e edições manuais.

## Instrução para o próximo executor

> Executa a fase descrita neste handoff no projeto Tactics. Começa por ler `CLAUDE.md`, inspecionar o estado local e estabelecer uma baseline dos testes e das partidas com bots. Implementa a ferramenta de simulações reproduzíveis reutilizando o núcleo e o conteúdo reais. Mede resultados por raça, posição, mapa e modo, corrige bugs demonstrados e compara ajustes estratégicos com a baseline. Pergunta-me sobre decisões de design por definir antes de alterar regras ou estatísticas, continuando o trabalho independente dessas respostas. Preserva UI, conteúdo, saves e trabalho local existente. Não implementes multiplayer nem habilidades/críticos nesta fase. Valida em mobile real quando houver dispositivo e distingue claramente trabalho concluído, problemas abertos e validações pendentes.

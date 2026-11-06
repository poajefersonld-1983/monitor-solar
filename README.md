# Monitor Solar

Potência instantânea dos dois inversores na barra, consultada no Home Assistant a cada 15 segundos. O indicador mostra watts e a porcentagem de 4.400 W. Uma leitura de 1.448 W aparece como **1.448 W · 32,9%**. Valores acima de 4.400 W aparecem acima de 100%.

## Windows 11

1. Extraia o ZIP para uma pasta permanente e abra `windows/MonitorSolar.exe`. A primeira execução abre a configuração.
2. Salve a conexão conforme as instruções abaixo. O indicador aparece perto do relógio. Clique nele para abrir a configuração ou use o botão direito para trocar a cor, atualizar ou sair.
3. Para iniciar com o Windows, crie um atalho para o executável na pasta aberta com **Win + R** e `shell:startup`.

A exibição segue o monitor de tokens e usa uma sobreposição na barra principal. Ela pode disputar espaço quando a barra está cheia. Quando a barra fica oculta ou a posição não está disponível, use o ícone na área de notificação. Passe o mouse para ver a leitura ou o status.

O executável usa o .NET Framework do Windows. Para recompilar a partir do código-fonte, execute `windows\compilar.cmd`.

## Linux com KDE Plasma 6

1. Extraia o ZIP e execute `bash linux/instalar.sh` na pasta extraída. O instalador requer `kpackagetool6` e PySide6 para o Python 3 do sistema. Em Debian ou Ubuntu, o pacote costuma se chamar `python3-pyside6.qtwidgets`. No Fedora, `python3-pyside6`.
2. No modo de edição do painel, abra **Adicionar widgets**, procure **Monitor Solar** e arraste o widget para a barra.
3. Clique no indicador e escolha **Configurar / cor**.

## Cores

Escolha entre roxo, azul, ciano, verde, amarelo, laranja, vermelho, rosa, branco e preto. **Outra cor…** abre o seletor personalizado. No KDE, escolha a cor em **Configurar / cor** e clique em **Salvar**. No Windows, use o menu **Trocar cor** no botão direito do indicador.

## Conectar ao Home Assistant

- Informe a URL que você abre no navegador, por exemplo `http://homeassistant.local:8123`.
- No Home Assistant, abra o seu perfil, vá a **Segurança** e crie um **token de acesso de longa duração**. Cole-o na janela do monitor. A [documentação da API](https://developers.home-assistant.io/docs/api/rest/) descreve a autenticação.
- Deixe os dois campos de sensor vazios para localizar **Potência Solar Total**. Se houver mais de um resultado ou se o nome estiver diferente, informe o ID da entidade total no primeiro campo. Para somar no aplicativo, preencha os dois IDs em **Ferramentas do desenvolvedor → Estados**. Cada entidade precisa medir potência em W ou kW.
- O monitor indica indisponibilidade se uma das leituras necessárias falhar. Zero watts é exibido como zero quando informado pelo Home Assistant.

As configurações ficam no computador. O Windows as protege com DPAPI no perfil do usuário em `%LOCALAPPDATA%\MonitorSolar\settings.dat`. No Linux, o token fica no arquivo privado `~/.config/monitor-solar/settings.json` (modo 0600 ou sob `XDG_CONFIG_HOME`). Para desinstalar, feche o monitor e exclua o executável e o atalho no Windows. No Plasma, remova o widget do painel e execute `kpackagetool6 --type Plasma/Applet --remove io.github.poajefersonld1983.monitor-solar`. Exclua o arquivo de configuração correspondente se quiser remover as credenciais locais.

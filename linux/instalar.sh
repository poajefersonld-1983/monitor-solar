#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
command -v kpackagetool6 >/dev/null || { echo 'Instale o KDE Plasma 6 e kpackagetool6.' >&2; exit 1; }
/usr/bin/python3 -c 'import PySide6' || { echo 'Instale PySide6 para o Python 3 do sistema.' >&2; exit 1; }
widget=io.github.poajefersonld1983.monitor-solar
if kpackagetool6 --type Plasma/Applet --show "$widget" >/dev/null 2>&1; then
    kpackagetool6 --type Plasma/Applet --upgrade package
else
    kpackagetool6 --type Plasma/Applet --install package
fi
echo 'No painel do Plasma, entre no modo de edição, adicione o widget Monitor Solar e clique em Configurar / cor.'

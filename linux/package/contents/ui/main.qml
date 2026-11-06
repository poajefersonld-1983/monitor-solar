import QtQuick
import QtQuick.Layouts
import org.kde.plasma.plasmoid
import org.kde.plasma.core as PlasmaCore
import org.kde.plasma.components as PC
import org.kde.plasma.plasma5support as Plasma5Support

PlasmoidItem {
    id: root

    property var snapshot: ({})
    property string readCommand: ""
    property bool configuring: false
    property string cachedUrl: ""
    property string cachedEntity: ""

    readonly property var locale: Qt.locale("pt_BR")
    readonly property bool available: typeof snapshot.watts === "number"
    readonly property string powerText: available ? Number(snapshot.watts).toLocaleString(locale, "f", 0) + " W" : "Solar —"
    readonly property string percentText: available ? Number(snapshot.percent).toLocaleString(locale, "f", 1) + "%" : ""
    readonly property string statusText: snapshot.error || (available ? "100% = 4.400 W"
        : snapshot.configured ? "Consultando o Home Assistant…" : "Configure o Home Assistant.")
    readonly property color indicatorColor: snapshot.color || "#ffd700"
    readonly property bool vertical: Plasmoid.formFactor === PlasmaCore.Types.Vertical
    readonly property string scriptPath: decodeURIComponent(Qt.resolvedUrl("../code/solar.py").toString().replace(/^file:\/\//, ""))
    readonly property string backend: "/usr/bin/python3 -B " + quote(scriptPath)
    readonly property string configCommand: backend + " --configure"

    Plasmoid.icon: "weather-clear"
    preferredRepresentation: compactRepresentation
    toolTipMainText: "Monitor Solar"
    toolTipSubText: available ? powerText + " · " + percentText + "\n" + statusText : statusText
    toolTipTextFormat: Text.PlainText

    function quote(value) {
        return "'" + value.replace(/'/g, "'\\''") + "'";
    }

    function refresh() {
        if (readCommand || configuring) return;
        readCommand = backend + " " + quote(cachedUrl) + " " + quote(cachedEntity);
        executable.connectSource(readCommand);
    }

    Plasma5Support.DataSource {
        id: executable
        engine: "executable"
        connectedSources: []
        onNewData: function(source, data) {
            disconnectSource(source);
            if (source === root.configCommand) {
                root.configuring = false;
                if (data["exit code"] === 0) {
                    root.cachedUrl = "";
                    root.cachedEntity = "";
                    root.readCommand = "";
                    root.refresh();
                } else if (data["exit code"] !== 1 || String(data.stderr || "").length) {
                    root.snapshot = {color: root.snapshot.color, error: "Falha ao abrir a configuração. Verifique o PySide6."};
                    poll.restart();
                } else {
                    poll.restart();
                }
                return;
            }
            if (source !== root.readCommand) return;
            root.readCommand = "";
            try {
                if (data["exit code"] !== 0) throw new Error("backend");
                root.snapshot = JSON.parse(data.stdout);
                root.cachedUrl = root.snapshot.url || "";
                root.cachedEntity = root.snapshot.entity || "";
            } catch (error) {
                root.snapshot = {color: root.snapshot.color, error: "Falha ao ler o monitor. Verifique o Python 3."};
            }
            poll.restart();
        }
    }

    Timer { id: poll; interval: 15000; onTriggered: root.refresh() }
    Component.onCompleted: refresh()

    compactRepresentation: Item {
        Layout.minimumWidth: root.vertical ? 0 : reading.implicitWidth + 16
        Layout.preferredWidth: Layout.minimumWidth
        Layout.minimumHeight: root.vertical ? reading.implicitHeight + 10 : 0

        PC.Label {
            id: reading
            anchors.centerIn: parent
            text: root.powerText + (root.available ? (root.vertical ? "\n" : "  ·  ") + root.percentText : "")
            color: root.indicatorColor
            horizontalAlignment: Text.AlignHCenter
            textFormat: Text.PlainText
        }
        MouseArea {
            anchors.fill: parent
            cursorShape: Qt.PointingHandCursor
            onClicked: root.expanded = !root.expanded
        }
    }

    fullRepresentation: ColumnLayout {
        Layout.minimumWidth: 320
        Layout.minimumHeight: 170
        PC.Label {
            Layout.alignment: Qt.AlignHCenter
            text: "Potência solar total"
        }
        PC.Label {
            Layout.alignment: Qt.AlignHCenter
            text: root.powerText + (root.available ? "  ·  " + root.percentText : "")
            color: root.indicatorColor
            font.pixelSize: 24
        }
        PC.Label {
            Layout.fillWidth: true
            horizontalAlignment: Text.AlignHCenter
            wrapMode: Text.WordWrap
            textFormat: Text.PlainText
            text: root.statusText
        }
        RowLayout {
            Layout.alignment: Qt.AlignHCenter
            PC.Button {
                text: "Configurar / cor"
                enabled: !root.configuring && !root.readCommand
                onClicked: {
                    root.configuring = true;
                    executable.connectSource(root.configCommand);
                }
            }
            PC.Button {
                text: "Atualizar"
                enabled: !root.configuring && !root.readCommand
                onClicked: root.refresh()
            }
        }
    }
}

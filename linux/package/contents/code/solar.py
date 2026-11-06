import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import Request, HTTPRedirectHandler, build_opener

CAPACITY = 4400
DEFAULT_COLOR = "#ffd700"
PALETTE = (
    ("Roxo", "#9b59ff"), ("Azul", "#4169e1"), ("Ciano", "#40e0d0"),
    ("Verde", "#32cd32"), ("Amarelo", DEFAULT_COLOR), ("Laranja", "#ff8c00"),
    ("Vermelho", "#dc143c"), ("Rosa", "#ff69b4"), ("Branco", "#ffffff"),
    ("Preto", "#000000"),
)
CONFIG = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config") / "monitor-solar" / "settings.json"
DEFAULTS = {"url": "", "token": "", "first": "", "second": "", "color": DEFAULT_COLOR}
ENTITY = re.compile(r"sensor\.[a-z0-9_]+")


class MonitorError(Exception):
    pass


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def load():
    try:
        return {**DEFAULTS, **json.loads(CONFIG.read_text(encoding="utf-8"))}
    except FileNotFoundError:
        return DEFAULTS.copy()


def save(settings):
    CONFIG.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=CONFIG.parent, delete=False) as output:
        temp = Path(output.name)
        try:
            json.dump(settings, output, ensure_ascii=False)
            output.close()
            temp.replace(CONFIG)
        finally:
            temp.unlink(missing_ok=True)


def validate(settings):
    try:
        url = urlsplit(settings["url"])
        valid_url = url.scheme in ("http", "https") and url.hostname and not (
            url.username is not None or url.query or url.fragment
        )
        _ = url.port
    except ValueError:
        valid_url = False
    if not valid_url:
        raise MonitorError("Informe o endereço HTTP ou HTTPS do Home Assistant.")
    if not settings["token"] or any(c.isspace() for c in settings["token"]):
        raise MonitorError("Informe um token válido do Home Assistant.")
    first, second = settings["first"], settings["second"]
    if (first and not ENTITY.fullmatch(first)) or (
        second and (not ENTITY.fullmatch(second) or not first or first == second)
    ):
        raise MonitorError("Informe IDs de sensores válidos e distintos.")


def fetch(settings, entity=""):
    path = "/api/states" + ("/" + entity if entity else "")
    request = Request(settings["url"] + path, headers={"Authorization": "Bearer " + settings["token"]})
    try:
        with build_opener(NoRedirect()).open(request, timeout=6) as response:
            return json.load(response)
    except HTTPError as error:
        if error.code in (401, 403):
            raise MonitorError("Acesso negado. Confira o token.") from None
        if error.code == 404:
            raise MonitorError("Sensor ou endereço do Home Assistant não encontrado.") from None
        raise MonitorError(f"Falha do Home Assistant (HTTP {error.code}).") from None
    except (URLError, TimeoutError):
        raise MonitorError("Falha ao consultar o Home Assistant.") from None


def watts(sensor):
    unit = sensor["attributes"].get("unit_of_measurement")
    if unit not in ("W", "kW"):
        raise MonitorError("O sensor precisa medir potência em W ou kW.")
    try:
        value = float(sensor["state"]) * (1000 if unit == "kW" else 1)
    except (TypeError, ValueError):
        raise MonitorError("Leitura solar indisponível.") from None
    if not math.isfinite(value) or value < 0:
        raise MonitorError("Leitura solar indisponível.")
    return value


def read(settings, cached_url="", cached_entity=""):
    validate(settings)
    first, second = settings["first"], settings["second"]
    detected = ""
    if not first:
        if settings["url"] == cached_url and ENTITY.fullmatch(cached_entity):
            detected = cached_entity
            total = watts(fetch(settings, detected))
        else:
            candidates = [
                sensor for sensor in fetch(settings)
                if ENTITY.fullmatch(sensor.get("entity_id", "")) and (
                    sensor["entity_id"] == "sensor.potencia_solar_total" or
                    sensor.get("attributes", {}).get("friendly_name", "").casefold() == "potência solar total"
                )
            ]
            if len(candidates) != 1:
                raise MonitorError("Informe o ID do sensor total ou os IDs dos dois inversores.")
            detected = candidates[0]["entity_id"]
            total = watts(candidates[0])
    else:
        total = watts(fetch(settings, first))
        if second:
            total += watts(fetch(settings, second))
    if not math.isfinite(total):
        raise MonitorError("Leitura solar indisponível.")
    return {"watts": total, "percent": total / CAPACITY * 100, "entity": detected, "url": settings["url"]}


def configure():
    from PySide6.QtWidgets import (
        QApplication, QColorDialog, QComboBox, QDialog, QDialogButtonBox, QFormLayout,
        QHBoxLayout, QLabel, QLineEdit, QMessageBox, QPushButton,
    )
    from PySide6.QtGui import QColor, QIcon, QPixmap

    app = QApplication([])
    try:
        settings = load()
    except (ValueError, OSError, TypeError):
        settings = DEFAULTS.copy()
    dialog = QDialog()
    dialog.setWindowTitle("Monitor Solar · Home Assistant")
    dialog.setMinimumWidth(510)
    form = QFormLayout(dialog)
    fields = {}
    for key, label in (
        ("url", "Endereço do Home Assistant"), ("token", "Token de longa duração"),
        ("first", "Sensor total ou inversor 1 (ID)"), ("second", "Inversor 2 (ID, opcional)"),
    ):
        fields[key] = QLineEdit(settings[key])
        form.addRow(label, fields[key])
    fields["url"].setPlaceholderText("http://homeassistant.local:8123")
    fields["token"].setEchoMode(QLineEdit.EchoMode.Password)
    note = QLabel("Campos de sensor vazios procuram “Potência Solar Total”.")
    note.setWordWrap(True)
    form.addRow(note)
    colors = QComboBox()

    def add_color(label, value):
        swatch = QPixmap(16, 16)
        swatch.fill(QColor(value))
        colors.addItem(QIcon(swatch), label, value)

    for label, value in PALETTE:
        add_color(label, value)

    def select_color(value):
        index = colors.findData(value)
        if index < 0:
            if colors.count() > len(PALETTE):
                colors.removeItem(len(PALETTE))
            add_color(f"Personalizada ({value})", value)
            index = len(PALETTE)
        colors.setCurrentIndex(index)

    select_color(settings["color"])
    color_button = QPushButton("Outra cor…")
    color_row = QHBoxLayout()
    color_row.addWidget(colors, 1)
    color_row.addWidget(color_button)
    form.addRow("Cor do texto no painel", color_row)

    def choose_color():
        selected = QColorDialog.getColor(QColor(colors.currentData()), dialog, "Cor do indicador")
        if selected.isValid():
            select_color(selected.name())

    def submit():
        next_settings = {**settings, **{key: field.text().strip() for key, field in fields.items()}}
        next_settings["url"] = next_settings["url"].rstrip("/")
        next_settings["color"] = colors.currentData()
        try:
            validate(next_settings)
            save(next_settings)
        except MonitorError as error:
            QMessageBox.warning(dialog, "Monitor Solar", str(error))
            return
        except OSError:
            QMessageBox.warning(dialog, "Monitor Solar", "Não foi possível salvar as configurações.")
            return
        dialog.accept()

    buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
    form.addRow(buttons)
    buttons.accepted.connect(submit)
    buttons.rejected.connect(dialog.reject)
    color_button.clicked.connect(choose_color)
    return 0 if dialog.exec() == QDialog.DialogCode.Accepted else 1


def report(args):
    result = {"color": DEFAULT_COLOR}
    try:
        settings = load()
        result["color"] = settings["color"]
        result["configured"] = bool(settings["url"] and settings["token"])
        if result["configured"]:
            result.update(read(settings, *args))
    except MonitorError as error:
        result["error"] = str(error)
    except (ValueError, TypeError, KeyError, OSError):
        result["error"] = "Não foi possível ler a configuração ou a resposta do Home Assistant."
    print(json.dumps(result, allow_nan=False))


if __name__ == "__main__":
    if sys.argv[1:] == ["--configure"]:
        sys.exit(configure())
    report(sys.argv[1:])

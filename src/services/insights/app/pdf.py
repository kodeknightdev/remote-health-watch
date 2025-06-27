"""Weekly patient report rendered with ReportLab. Identifies the patient by surrogate id only."""

import io
from dataclasses import dataclass, field
from datetime import date, timedelta

from reportlab.graphics.charts.linecharts import HorizontalLineChart
from reportlab.graphics.shapes import Drawing, String
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

UNITS = {
    "HeartRate": "bpm",
    "SpO2": "%",
    "RespiratoryRate": "/min",
    "Temperature": "°C",
    "BloodPressure": "mmHg (sys)",
    "Glucose": "mg/dL",
}


@dataclass
class VitalWeek:
    vital: str
    daily_avg: list[float | None]
    minimum: float | None
    average: float | None
    maximum: float | None
    readings: int


@dataclass
class ReportData:
    patient_ref: str
    week_start: date
    vitals: list[VitalWeek]
    daily_max_news2: list[int | None]
    alerts_by_severity: dict[str, int]
    alerts_acknowledged: int
    median_minutes_to_ack: float | None
    escalated: int
    daily_wear: list[float | None]
    notes: list[str] = field(default_factory=list)


def _days(week_start: date) -> list[str]:
    return [(week_start + timedelta(days=i)).strftime("%a %d") for i in range(7)]


def _trend_chart(v: VitalWeek, labels: list[str]) -> Drawing:
    d = Drawing(170 * mm, 38 * mm)
    values = [x if x is not None else 0 for x in v.daily_avg]
    chart = HorizontalLineChart()
    chart.x, chart.y = 12 * mm, 6 * mm
    chart.width, chart.height = 150 * mm, 26 * mm
    chart.data = [values]
    chart.categoryAxis.categoryNames = labels
    chart.categoryAxis.labels.fontSize = 6
    chart.valueAxis.labels.fontSize = 6
    present = [x for x in v.daily_avg if x is not None]
    if present:
        lo, hi = min(present), max(present)
        pad = max(1.0, (hi - lo) * 0.2)
        chart.valueAxis.valueMin, chart.valueAxis.valueMax = lo - pad, hi + pad
    chart.lines[0].strokeColor = colors.HexColor("#1565c0")
    chart.lines[0].strokeWidth = 1.2
    d.add(chart)
    d.add(String(0, 35 * mm, f"{v.vital} daily average ({UNITS.get(v.vital, '')})", fontSize=8))
    return d


def _fmt(x: float | None) -> str:
    return "–" if x is None else f"{x:.1f}"


def render(data: ReportData) -> bytes:
    buf = io.BytesIO()
    doc = SimpleDocTemplate(
        buf,
        pagesize=A4,
        leftMargin=18 * mm,
        rightMargin=18 * mm,
        topMargin=16 * mm,
        bottomMargin=16 * mm,
        title="remote-health-watch weekly report",
        author="remote-health-watch",
    )
    styles = getSampleStyleSheet()
    labels = _days(data.week_start)
    week_end = data.week_start + timedelta(days=6)
    grid = TableStyle(
        [
            ("GRID", (0, 0), (-1, -1), 0.25, colors.grey),
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#eceff1")),
            ("FONTSIZE", (0, 0), (-1, -1), 8),
        ]
    )

    story = [
        Paragraph("Weekly remote monitoring report", styles["Title"]),
        Paragraph(
            f"Patient ref {data.patient_ref} · {data.week_start:%d %b %Y} – {week_end:%d %b %Y}", styles["Normal"]
        ),
        Paragraph(
            "<i>Demo system with synthetic data. Not a medical device and not for clinical decisions.</i>",
            styles["Normal"],
        ),
        Spacer(1, 6 * mm),
        Paragraph("Vital signs", styles["Heading2"]),
    ]
    rows = [["Vital", "Min", "Avg", "Max", "Readings"]]
    for v in data.vitals:
        rows.append([v.vital, _fmt(v.minimum), _fmt(v.average), _fmt(v.maximum), str(v.readings)])
    story += [
        Table(rows, hAlign="LEFT", colWidths=[40 * mm, 22 * mm, 22 * mm, 22 * mm, 22 * mm], style=grid),
        Spacer(1, 4 * mm),
    ]
    for v in data.vitals:
        if any(x is not None for x in v.daily_avg):
            story.append(_trend_chart(v, labels))

    story += [Paragraph("NEWS2 and device wear", styles["Heading2"])]
    wear = ["–" if w is None else f"{w * 100:.0f}%" for w in data.daily_wear]
    news = ["–" if n is None else str(n) for n in data.daily_max_news2]
    story += [
        Table([[""] + labels, ["Max NEWS2"] + news, ["Wear time"] + wear], hAlign="LEFT", style=grid),
        Spacer(1, 4 * mm),
    ]

    story += [Paragraph("Alerts", styles["Heading2"])]
    alert_rows = [["Severity", "Count"]] + [[k, str(v)] for k, v in data.alerts_by_severity.items()]
    story.append(Table(alert_rows, hAlign="LEFT", colWidths=[40 * mm, 22 * mm], style=grid))
    ack = "n/a" if data.median_minutes_to_ack is None else f"{data.median_minutes_to_ack:.1f} min"
    story.append(
        Paragraph(
            f"Acknowledged {data.alerts_acknowledged}, escalated beyond the nurse {data.escalated}, "
            f"median time to acknowledge {ack}.",
            styles["Normal"],
        )
    )
    for note in data.notes:
        story.append(Paragraph(note, styles["Normal"]))

    doc.build(story)
    return buf.getvalue()

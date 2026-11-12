"""Source for Diagrams/MediPass_Class_Diagram_Part3_Record_Chat.png.

This script is the editable source of the diagram. It renders the record/chat class diagram
with the ADR-0005 (Accepted 2026-11-11) relationships and no external diagram tool is needed.

Run from the repository root:

    python Diagrams/src/render_class_diagram_part3.py

Outputs:

    Diagrams/MediPass_Class_Diagram_Part3_Record_Chat.png
    Diagrams/src/MediPass_Class_Diagram_Part3_Record_Chat.svg

The layout uses a 1600 x 1160 pixel canvas with the origin at the top-left (y grows downward).
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
from matplotlib.font_manager import FontProperties
from matplotlib.patches import Polygon, Rectangle

REPO_ROOT = Path(__file__).resolve().parents[2]
OUT_PNG = REPO_ROOT / "Diagrams" / "MediPass_Class_Diagram_Part3_Record_Chat.png"
OUT_SVG = Path(__file__).resolve().parent / "MediPass_Class_Diagram_Part3_Record_Chat.svg"

WIDTH = 1600
HEIGHT = 1160
LINE_HEIGHT = 19
TITLE_HEIGHT = 34
BOX_PADDING = 10


def _font(bold: bool) -> FontProperties:
    """Prefer Arial (the original diagram's look), fall back to DejaVu Sans."""
    candidate = Path(r"C:\Windows\Fonts") / ("arialbd.ttf" if bold else "arial.ttf")
    if candidate.exists():
        return FontProperties(fname=str(candidate))
    return FontProperties(family="DejaVu Sans", weight="bold" if bold else "normal")


FONT = _font(bold=False)
FONT_BOLD = _font(bold=True)


@dataclass
class Box:
    name: str
    x: float
    y: float
    width: float
    attributes: list[str] = field(default_factory=list)
    methods: list[str] = field(default_factory=list)

    @property
    def height(self) -> float:
        lines = len(self.attributes) + len(self.methods)
        gap = 6 if self.attributes and self.methods else 0
        return max(50, TITLE_HEIGHT + lines * LINE_HEIGHT + gap + BOX_PADDING)

    @property
    def right(self) -> float:
        return self.x + self.width

    @property
    def bottom(self) -> float:
        return self.y + self.height

    @property
    def center_x(self) -> float:
        return self.x + self.width / 2


@dataclass(frozen=True)
class Relationship:
    source: tuple[float, float]
    target: tuple[float, float]
    label: str
    label_at: tuple[float, float]
    source_multiplicity: str
    target_multiplicity: str
    source_at: tuple[float, float]
    target_at: tuple[float, float]
    composition: bool = False


BOXES = [
    Box("Patient", 700, 40, 200),
    Box("Appointment", 120, 270, 280),
    Box(
        "MedicalRecord",
        540,
        170,
        430,
        attributes=[
            "- id: Guid",
            "- patientId: Guid {unique}",
            "- createdAtUtc: DateTime",
        ],
        methods=[
            "+ getHistory(grantId: Guid): Diagnosis [*]",
            "+ addDiagnosis(d: Diagnosis): void",
            "+ addReport(r: Report): void",
        ],
    ),
    Box(
        "Diagnosis",
        500,
        440,
        430,
        attributes=[
            "- id: Guid",
            "- medicalRecordId: Guid",
            "- appointmentId: int",
            "- supersedesDiagnosisId: Guid [0..1]",
            "- description: string {encrypted}",
            "- diagnosedAtUtc: DateTime",
        ],
        methods=[
            "+ addPrescription(p: Prescription): void",
            "+ getDescription(): string",
        ],
    ),
    Box(
        "Prescription",
        500,
        730,
        430,
        attributes=[
            "- id: Guid",
            "- diagnosisId: Guid",
            "- supersedesPrescriptionId: Guid [0..1]",
            "- medicineName: string {encrypted}",
            "- dosage: string {encrypted}",
            "- notes: string [0..1] {encrypted}",
        ],
        methods=[
            "+ getSummary(): string",
        ],
    ),
    Box(
        "Report",
        1070,
        390,
        400,
        attributes=[
            "- id: Guid",
            "- medicalRecordId: Guid",
            "- uploadedByUserId: Guid",
            "- storedFileName: string",
            "- contentType: string",
            "- sizeBytes: long",
            "- uploadedAtUtc: DateTime",
        ],
        methods=[
            "+ isAllowedType(): boolean",
        ],
    ),
    Box(
        "Conversation",
        1040,
        40,
        420,
        attributes=[
            "- id: Guid",
            "- createdAt: DateTime",
        ],
        methods=[
            "+ addMessage(senderId: Guid, text: string): Message",
        ],
    ),
    Box(
        "Message",
        1040,
        190,
        420,
        attributes=[
            "- id: Guid",
            "- senderUserId: Guid",
            "- content: string",
            "- sentAt: DateTime",
            "- isRead: boolean = false",
        ],
        methods=[
            "+ markRead(): void",
        ],
    ),
]


RELATIONSHIPS = [
    Relationship(
        source=(800, 90),
        target=(750, 170),
        label="owns",
        label_at=(745, 125),
        source_multiplicity="1",
        target_multiplicity="1",
        source_at=(815, 105),
        target_at=(735, 155),
        composition=True,
    ),
    Relationship(
        source=(700, 65),
        target=(260, 270),
        label="books",
        label_at=(455, 160),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(690, 80),
        target_at=(275, 255),
    ),
    Relationship(
        source=(900, 65),
        target=(1040, 85),
        label="takes part in",
        label_at=(970, 55),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(915, 78),
        target_at=(1025, 98),
    ),
    Relationship(
        source=(750, 334),
        target=(750, 440),
        label="contains",
        label_at=(760, 385),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(735, 355),
        target_at=(735, 425),
        composition=True,
    ),
    Relationship(
        source=(400, 300),
        target=(500, 460),
        label="results in",
        label_at=(430, 390),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(410, 315),
        target_at=(485, 445),
    ),
    Relationship(
        source=(970, 300),
        target=(1070, 440),
        label="holds",
        label_at=(995, 340),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(985, 315),
        target_at=(1055, 425),
        composition=True,
    ),
    Relationship(
        source=(750, 642),
        target=(750, 730),
        label="includes",
        label_at=(760, 685),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(735, 665),
        target_at=(735, 715),
        composition=True,
    ),
    Relationship(
        source=(1250, 147),
        target=(1250, 190),
        label="has",
        label_at=(1265, 170),
        source_multiplicity="1",
        target_multiplicity="0..*",
        source_at=(1235, 155),
        target_at=(1235, 183),
        composition=True,
    ),
]


def draw_box(ax: plt.Axes, box: Box) -> None:
    ax.add_patch(
        Rectangle(
            (box.x, box.y),
            box.width,
            box.height,
            facecolor="white",
            edgecolor="black",
            linewidth=1.2,
            zorder=3,
        )
    )
    ax.text(
        box.center_x,
        box.y + 12,
        box.name,
        fontproperties=FONT_BOLD,
        fontsize=13,
        ha="center",
        va="top",
        zorder=4,
    )
    ax.plot(
        [box.x, box.right],
        [box.y + TITLE_HEIGHT, box.y + TITLE_HEIGHT],
        color="black",
        linewidth=1.0,
        zorder=4,
    )

    y = box.y + TITLE_HEIGHT + 9
    for attribute in box.attributes:
        ax.text(
            box.x + 12,
            y,
            attribute,
            fontproperties=FONT,
            fontsize=10.5,
            ha="left",
            va="top",
            zorder=4,
        )
        y += LINE_HEIGHT

    if box.attributes and box.methods:
        y += 6

    for method in box.methods:
        ax.text(
            box.x + 12,
            y,
            method,
            fontproperties=FONT,
            fontsize=10.5,
            ha="left",
            va="top",
            zorder=4,
        )
        y += LINE_HEIGHT


def draw_diamond(
    ax: plt.Axes, source: tuple[float, float], target: tuple[float, float], size: float = 16, width: float = 9
) -> None:
    sx, sy = source
    tx, ty = target
    dx, dy = tx - sx, ty - sy
    length = math.hypot(dx, dy)
    ux, uy = dx / length, dy / length

    tip = (sx + ux * size, sy + uy * size)
    mid = (sx + ux * size / 2, sy + uy * size / 2)
    left = (mid[0] - uy * width / 2, mid[1] + ux * width / 2)
    right = (mid[0] + uy * width / 2, mid[1] - ux * width / 2)

    ax.add_patch(
        Polygon(
            [(sx, sy), left, tip, right],
            closed=True,
            facecolor="black",
            edgecolor="black",
            linewidth=1.0,
            zorder=5,
        )
    )


def draw_relationship(ax: plt.Axes, relationship: Relationship) -> None:
    sx, sy = relationship.source
    tx, ty = relationship.target

    ax.plot([sx, tx], [sy, ty], color="black", linewidth=1.2, zorder=1)

    if relationship.composition:
        draw_diamond(ax, relationship.source, relationship.target)

    ax.text(
        relationship.label_at[0],
        relationship.label_at[1],
        relationship.label,
        fontproperties=FONT,
        fontsize=10.5,
        ha="center",
        va="center",
        bbox={"facecolor": "white", "edgecolor": "none", "pad": 1.5},
        zorder=6,
    )

    for text, position in (
        (relationship.source_multiplicity, relationship.source_at),
        (relationship.target_multiplicity, relationship.target_at),
    ):
        ax.text(
            position[0],
            position[1],
            text,
            fontproperties=FONT,
            fontsize=10,
            ha="center",
            va="center",
            bbox={"facecolor": "white", "edgecolor": "none", "pad": 1.0},
            zorder=6,
        )


def draw_note(ax: plt.Axes) -> None:
    x, y, width, height = 100, 980, 1400, 120
    ax.add_patch(
        Rectangle(
            (x, y),
            width,
            height,
            facecolor="white",
            edgecolor="black",
            linewidth=1.0,
            zorder=3,
        )
    )

    lines = [
        "Part 3: medical record and chat (stretch). Patient and Appointment collapsed.",
        "Notation: - private, + public; [0..1] optional (no marker = required); [*] list; filled diamond = composition.",
        "ADR-0005 (Accepted 2026-11-11): one MedicalRecord per patient; Diagnosis.MedicalRecordId and",
        "AppointmentId are required, so a diagnosis is always visit-scoped; a diagnosis has 0..* prescriptions.",
        "No update/delete: corrections are new rows linked by supersedesDiagnosisId / supersedesPrescriptionId.",
        "{encrypted} = Data Protection ciphertext (diagnosis and prescription text); reports are record-level,",
        "with uploader attribution and no appointment FK.",
    ]

    line_y = y + 12
    for line in lines:
        ax.text(
            x + 12,
            line_y,
            line,
            fontproperties=FONT,
            fontsize=9.5,
            ha="left",
            va="top",
            zorder=4,
        )
        line_y += 14


def main() -> None:
    figure = plt.figure(figsize=(WIDTH / 100, HEIGHT / 100), dpi=100, facecolor="white")
    axes = figure.add_axes([0, 0, 1, 1])
    axes.set_xlim(0, WIDTH)
    axes.set_ylim(HEIGHT, 0)
    axes.axis("off")

    for relationship in RELATIONSHIPS:
        draw_relationship(axes, relationship)

    for box in BOXES:
        draw_box(axes, box)

    draw_note(axes)

    figure.savefig(OUT_PNG, dpi=100, facecolor="white")
    figure.savefig(OUT_SVG, facecolor="white")
    plt.close(figure)

    # Matplotlib leaves trailing spaces on generated SVG path data; keep the source clean.
    svg_text = OUT_SVG.read_text(encoding="utf-8")
    OUT_SVG.write_text(
        "\n".join(line.rstrip() for line in svg_text.splitlines()) + "\n",
        encoding="utf-8",
    )

    print(f"wrote {OUT_PNG}")
    print(f"wrote {OUT_SVG}")


if __name__ == "__main__":
    main()

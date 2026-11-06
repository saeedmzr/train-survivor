#!/usr/bin/env python3
"""Post-process the generated GDD: column widths, table rhythm, header repeat.

python-docx cannot set column widths from the create spec, and evenly split columns make
the Behaviour/Phases columns unreadable. Widths are keyed by the table's header signature.

Usage: python3 tools/format_gdd.py "Game Design Document_ Train Survivor.docx"
"""
import sys
from docx import Document
from docx.shared import Mm, Pt
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml.ns import qn
from docx.oxml import OxmlElement

WIDTHS = {
    ("Version", "Date", "Headline"): [18, 26, 126],
    ("Area", "v1.0", "v2.0 (current)"): [26, 50, 94],
    ("State", "Entered when", "Speed", "Guns", "Other"): [24, 54, 26, 18, 48],
    ("Beats", "Phase", "What happens"): [22, 30, 118],
    ("Property", "Value"): [50, 120],
    ("Wagon", "Unlocks at", "Gun slots", "Role"): [26, 26, 22, 96],
    ("Card", "Category", "Effect"): [44, 30, 96],
    ("Enemy", "HP", "Speed", "Dmg", "Behaviour and counter"): [30, 12, 16, 12, 100],
    ("Boss", "Biome", "HP", "Phases", "Weakness"): [32, 30, 14, 52, 42],
    ("Gun", "Ammo", "Type", "Behaviour"): [26, 34, 22, 88],
    ("Type", "On hit", "Kill"): [28, 62, 80],
    ("Element", "Design size", "Notes"): [42, 40, 88],
    ("Biome", "Premise", "Convoy", "Palette (first five)", "Beats"): [34, 46, 40, 36, 14],
    ("System", "State"): [34, 136],
    ("Need", "Path"): [58, 112],
}


def repeat_header(row):
    tr = row._tr
    trPr = tr.get_or_add_trPr()
    el = OxmlElement("w:tblHeader")
    el.set(qn("w:val"), "true")
    trPr.append(el)


def main(path):
    doc = Document(path)
    applied, skipped = 0, []
    for table in doc.tables:
        header = tuple(c.text.strip() for c in table.rows[0].cells)
        widths = WIDTHS.get(header)
        if not widths:
            skipped.append(header)
            continue
        table.autofit = False
        table.alignment = WD_TABLE_ALIGNMENT.LEFT
        # fixed layout + grid widths, otherwise Word/LibreOffice re-splits evenly
        tblPr = table._tbl.tblPr
        layout = OxmlElement("w:tblLayout")
        layout.set(qn("w:type"), "fixed")
        tblPr.append(layout)
        for i, w in enumerate(widths):
            table.columns[i].width = Mm(w)
            for cell in table.columns[i].cells:
                cell.width = Mm(w)
        repeat_header(table.rows[0])
        for row in table.rows:
            for cell in row.cells:
                for par in cell.paragraphs:
                    for run in par.runs:
                        run.font.size = Pt(9)
                par.paragraph_format.space_after = Pt(0)
        applied += 1
    # keep a heading with the paragraph/table that follows it
    for par in doc.paragraphs:
        if par.style.name.startswith("Heading"):
            par.paragraph_format.keep_with_next = True
    doc.save(path)
    print("tables formatted:", applied, "| unmatched:", skipped)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Game Design Document_ Train Survivor.docx")

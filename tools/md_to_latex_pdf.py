#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
import re
import shutil
import subprocess
import sys
from pathlib import Path


def latex_escape(text: str) -> str:
    """Escape LaTeX special chars in normal text context."""
    replacements = {
        "\\": r"\textbackslash{}",
        "{": r"\{",
        "}": r"\}",
        "#": r"\#",
        "%": r"\%",
        "&": r"\&",
        "_": r"\_",
        "^": r"\textasciicircum{}",
        "~": r"\textasciitilde{}",
    }
    return "".join(replacements.get(ch, ch) for ch in text)


_inline_code_re = re.compile(r"`([^`]+)`")
_bold_re = re.compile(r"\*\*([^*]+)\*\*")


def convert_inline(markdown: str) -> str:
    """Convert a subset of inline Markdown to LaTeX: **bold**, `code`."""

    def repl_code(match: re.Match) -> str:
        inner = match.group(1)
        return r"\texttt{" + latex_escape(inner) + "}"

    def repl_bold(match: re.Match) -> str:
        inner = match.group(1)
        return r"\textbf{" + latex_escape(inner) + "}"

    # Escape first, then re-inject inline formatting from original.
    # To keep it simple and safe, we apply formatting transforms first,
    # and escape the remaining plain text segments.

    # Split around code spans first.
    parts = []
    last = 0
    for m in _inline_code_re.finditer(markdown):
        if m.start() > last:
            parts.append(("text", markdown[last : m.start()]))
        parts.append(("code", m.group(1)))
        last = m.end()
    if last < len(markdown):
        parts.append(("text", markdown[last:]))

    out = []
    for kind, value in parts:
        if kind == "code":
            out.append(r"\texttt{" + latex_escape(value) + "}")
            continue

        # Bold inside non-code text.
        s = value
        segs = []
        last2 = 0
        for m2 in _bold_re.finditer(s):
            if m2.start() > last2:
                segs.append(("text", s[last2 : m2.start()]))
            segs.append(("bold", m2.group(1)))
            last2 = m2.end()
        if last2 < len(s):
            segs.append(("text", s[last2:]))

        for k2, v2 in segs:
            if k2 == "bold":
                out.append(r"\textbf{" + latex_escape(v2) + "}")
            else:
                out.append(latex_escape(v2))

    return "".join(out).strip()


def is_table_separator(line: str) -> bool:
    s = line.strip()
    if not (s.startswith("|") and s.endswith("|")):
        return False
    # Typical: | --- | --- |
    cells = [c.strip() for c in s.strip("|").split("|")]
    return all(re.fullmatch(r"-+", c.replace(":", "").strip()) is not None for c in cells)


def split_table_row(line: str) -> list[str]:
    s = line.strip().strip("|")
    return [c.strip() for c in s.split("|")]


def to_tabularx(rows: list[list[str]]) -> str:
    # rows includes header row already.
    col_count = max(len(r) for r in rows) if rows else 0
    if col_count == 0:
        return ""

    # Column spec: first column left, rest X to wrap.
    col_spec = "l" + " " + " ".join(["X"] * (col_count - 1))

    def fmt_row(r: list[str]) -> str:
        r2 = (r + [""] * col_count)[:col_count]
        return " & ".join(convert_inline(cell) for cell in r2) + r"\\"

    header = rows[0]
    body = rows[1:]

    lines = []
    lines.append(r"\begin{tabularx}{\linewidth}{@{}" + col_spec + r"@{}}")
    lines.append(r"\toprule")
    lines.append(fmt_row(header))
    lines.append(r"\midrule")
    for r in body:
        lines.append(fmt_row(r))
    lines.append(r"\bottomrule")
    lines.append(r"\end{tabularx}")
    return "\n".join(lines)


def md_to_latex(md_text: str) -> str:
    lines = md_text.splitlines()

    title = None
    out: list[str] = []

    out.append(r"\documentclass[UTF8]{ctexart}")
    out.append(r"\usepackage[a4paper,margin=2.2cm]{geometry}")
    out.append(r"\usepackage{hyperref}")
    out.append(r"\usepackage{booktabs}")
    out.append(r"\usepackage{tabularx}")
    out.append(r"\usepackage{xcolor}")
    out.append(r"\usepackage{listings}")
    out.append(r"\usepackage{adjustbox}")
    out.append(r"\usepackage{fontspec}")
    out.append(r"\setCJKmainfont{Noto Serif CJK SC}")
    out.append(r"\setCJKsansfont{Noto Sans CJK SC}")
    out.append(r"\setmonofont{DejaVu Sans Mono}")
    out.append(r"\lstset{")
    out.append(r"  basicstyle=\ttfamily\small,")
    out.append(r"  columns=fixed,")
    out.append(r"  keepspaces=true,")
    out.append(r"  breaklines=true,")
    out.append(r"  frame=single,")
    out.append(r"  rulecolor=\color{black!20},")
    out.append(r"  frameround=tttt,")
    out.append(r"  showstringspaces=false,")
    out.append(r"  xleftmargin=0.2cm,")
    out.append(r"  xrightmargin=0.2cm,")
    out.append(r"}")
    out.append("")

    i = 0
    in_code = False
    code_lang = ""
    code_buf: list[str] = []

    box_drawing_chars = set("┌┐└┘├┤┬┴┼│─")

    def is_box_drawing_diagram(buf: list[str]) -> bool:
        if not buf:
            return False
        sample = "\n".join(buf)
        hits = sum(1 for ch in sample if ch in box_drawing_chars)
        return hits >= 10  # heuristic: enough box-drawing glyphs to be a diagram

    def flush_paragraph(buf: list[str]):
        if not buf:
            return
        text = " ".join(s.strip() for s in buf if s.strip())
        if text:
            out.append(convert_inline(text))
            out.append("")
        buf.clear()

    para_buf: list[str] = []

    while i < len(lines):
        line = lines[i]

        # Code fences
        if in_code:
            if line.strip().startswith("```"):
                # close
                lang = code_lang.lower().strip()
                opt = ""
                if lang in {"cpp", "c++"}:
                    opt = "[language=C++]"
                elif lang in {"bash", "sh", "shell"}:
                    opt = "[language=bash]"
                if is_box_drawing_diagram(code_buf):
                    out.append(r"\begin{center}")
                    out.append(r"\begin{adjustbox}{max width=\textwidth,max height=0.85\textheight,keepaspectratio}")
                    out.append(
                        r"\begin{lstlisting}[basicstyle=\ttfamily\small,breaklines=false,frame=none,columns=fixed,keepspaces=true]")
                    out.extend(code_buf)
                    out.append(r"\end{lstlisting}")
                    out.append(r"\end{adjustbox}")
                    out.append(r"\end{center}")
                else:
                    out.append(r"\begin{lstlisting}" + opt)
                    out.extend(code_buf)
                    out.append(r"\end{lstlisting}")
                out.append("")
                in_code = False
                code_lang = ""
                code_buf = []
                i += 1
                continue

            code_buf.append(line)
            i += 1
            continue

        # Start code fence
        if line.strip().startswith("```"):
            flush_paragraph(para_buf)
            in_code = True
            code_lang = line.strip().lstrip("`").strip()
            code_buf = []
            i += 1
            continue

        # Horizontal rule
        if line.strip() == "---":
            flush_paragraph(para_buf)
            out.append(r"\par\bigskip\hrule\bigskip")
            out.append("")
            i += 1
            continue

        # Title / headings
        if line.startswith("# "):
            flush_paragraph(para_buf)
            title = line[2:].strip()
            i += 1
            continue
        if line.startswith("## "):
            flush_paragraph(para_buf)
            out.append(r"\section{" + latex_escape(line[3:].strip()) + "}")
            out.append("")
            i += 1
            continue
        if line.startswith("### "):
            flush_paragraph(para_buf)
            out.append(r"\subsection{" + latex_escape(line[4:].strip()) + "}")
            out.append("")
            i += 1
            continue

        # Blockquote
        if line.lstrip().startswith(">"):
            flush_paragraph(para_buf)
            q_lines = []
            while i < len(lines) and lines[i].lstrip().startswith(">"):
                q_lines.append(lines[i].lstrip()[1:].lstrip())
                i += 1
            out.append(r"\begin{quote}\small")
            out.append(r"\\".join(convert_inline(s) for s in q_lines if s.strip()))
            out.append(r"\end{quote}")
            out.append("")
            continue

        # Tables
        if line.strip().startswith("|") and i + 1 < len(lines) and is_table_separator(lines[i + 1]):
            flush_paragraph(para_buf)
            header = split_table_row(line)
            i += 2  # skip header + separator
            rows = [header]
            while i < len(lines):
                l2 = lines[i]
                if not l2.strip().startswith("|"):
                    break
                rows.append(split_table_row(l2))
                i += 1
            out.append(to_tabularx(rows))
            out.append("")
            continue

        # Bullet list
        if line.lstrip().startswith("- "):
            flush_paragraph(para_buf)
            out.append(r"\begin{itemize}")
            while i < len(lines) and lines[i].lstrip().startswith("- "):
                item = lines[i].lstrip()[2:]
                out.append(r"\item " + convert_inline(item))
                i += 1
            out.append(r"\end{itemize}")
            out.append("")
            continue

        # Blank line -> paragraph break
        if not line.strip():
            flush_paragraph(para_buf)
            i += 1
            continue

        # Default: accumulate paragraph
        para_buf.append(line)
        i += 1

    flush_paragraph(para_buf)

    if not title:
        title = "文档"

    # Inject title block at the beginning of body
    body = "\n".join(out)

    preamble_end = body.find("\n\n")
    # Simple: append title after preamble block (after initial packages/settings)
    # We inserted preamble lines first; place title after them.

    doc = []
    # Rebuild properly
    # Everything in `out` already includes preamble; split at first empty line after lstset if present.
    # We'll just construct explicitly.

    # Extract preamble lines: up to and including lstset block (we know where it starts)
    pre_lines = out[:]

    # Build final doc by rewriting: preamble part is fixed; so regenerate cleanly.
    doc = []
    doc.append(r"\documentclass[UTF8]{ctexart}")
    doc.append(r"\usepackage[a4paper,margin=2.2cm]{geometry}")
    doc.append(r"\usepackage{hyperref}")
    doc.append(r"\usepackage{booktabs}")
    doc.append(r"\usepackage{tabularx}")
    doc.append(r"\usepackage{xcolor}")
    doc.append(r"\usepackage{listings}")
    doc.append(r"\usepackage{adjustbox}")
    doc.append(r"\usepackage{fontspec}")
    doc.append(r"\setCJKmainfont{Noto Serif CJK SC}")
    doc.append(r"\setCJKsansfont{Noto Sans CJK SC}")
    doc.append(r"\setmonofont{DejaVu Sans Mono}")
    doc.append(r"\lstset{")
    doc.append(r"  basicstyle=\ttfamily\small,")
    doc.append(r"  columns=fixed,")
    doc.append(r"  keepspaces=true,")
    doc.append(r"  breaklines=true,")
    doc.append(r"  frame=single,")
    doc.append(r"  rulecolor=\color{black!20},")
    doc.append(r"  frameround=tttt,")
    doc.append(r"  showstringspaces=false,")
    doc.append(r"  xleftmargin=0.2cm,")
    doc.append(r"  xrightmargin=0.2cm,")
    doc.append(r"}")
    doc.append("")
    doc.append(r"\title{" + latex_escape(title) + "}")
    doc.append(r"\date{}")
    doc.append(r"\begin{document}")
    doc.append(r"\maketitle")
    doc.append("")

    # Body content: skip the initial preamble we previously appended; find first section or quote etc.
    # We can regenerate body by re-running conversion but without preamble, but keep it simple:
    # The `out` currently begins with preamble lines; strip them until after the first empty line.
    # We know preamble length = 1 (docclass) + packages... + lstset block + empty line.

    preamble_len = 1 + 6 + 1 + 3 + 1 + 11 + 1  # not reliable; so detect "\\lstset{" and its closing "}" then one blank
    # Instead: find index after the first occurrence of a blank line following a line that equals "}" and preceded by lstset.

    start_idx = 0
    seen_lstset = False
    for idx, l in enumerate(out):
        if l.strip() == r"\lstset{":
            seen_lstset = True
        if seen_lstset and l.strip() == "}" and idx + 1 < len(out) and out[idx + 1] == "":
            start_idx = idx + 2
            break

    body_lines = out[start_idx:]
    doc.extend(body_lines)
    doc.append(r"\end{document}")
    doc.append("")

    return "\n".join(doc)


def run(cmd: list[str], cwd: Path) -> None:
    proc = subprocess.run(cmd, cwd=str(cwd), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"Command failed ({proc.returncode}): {' '.join(cmd)}\n{proc.stdout}")


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: md_to_latex_pdf.py <input.md> [output.pdf]", file=sys.stderr)
        return 2

    repo_root = Path.cwd()
    md_path = Path(sys.argv[1]).resolve()
    out_pdf = Path(sys.argv[2]).resolve() if len(sys.argv) >= 3 else (repo_root / "UDP图传架构.pdf")

    if not md_path.exists():
        print(f"Input not found: {md_path}", file=sys.stderr)
        return 2

    build_dir = repo_root / ".tmp_md_to_pdf_build"
    if build_dir.exists():
        shutil.rmtree(build_dir)
    build_dir.mkdir(parents=True, exist_ok=True)

    try:
        md_text = md_path.read_text(encoding="utf-8")
        tex = md_to_latex(md_text)
        tex_path = build_dir / "doc.tex"
        tex_path.write_text(tex, encoding="utf-8")

        # Two passes for TOC/refs stability (even if we don't generate TOC).
        run(["xelatex", "-interaction=nonstopmode", "-halt-on-error", "-output-directory", str(build_dir), str(tex_path)], cwd=repo_root)
        run(["xelatex", "-interaction=nonstopmode", "-halt-on-error", "-output-directory", str(build_dir), str(tex_path)], cwd=repo_root)

        produced = build_dir / "doc.pdf"
        if not produced.exists():
            raise RuntimeError("xelatex did not produce doc.pdf")

        shutil.copyfile(produced, out_pdf)
        print(f"Wrote PDF: {out_pdf}")
        return 0
    finally:
        # Remove all intermediate files (including .tex) by deleting the build directory.
        if build_dir.exists():
            shutil.rmtree(build_dir)


if __name__ == "__main__":
    raise SystemExit(main())

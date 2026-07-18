import pdfplumber, os, sys
os.makedirs('pdf_text', exist_ok=True)
pdfs = [
    "Database fields and constants (Vision3D CR4).pdf",
    "Vieweb-install-note-V1.6.2.pdf",
    "Vieweb-release-note-V1.6.2.pdf",
    "Vieweb-user-guide-V1.6.2.pdf",
]
for p in pdfs:
    out = os.path.join("pdf_text", os.path.splitext(os.path.basename(p))[0] + ".txt")
    with pdfplumber.open(p) as pdf, open(out, "w", encoding="utf-8") as f:
        for i, page in enumerate(pdf.pages):
            f.write(f"\n===== PAGE {i+1} =====\n")
            try:
                text = page.extract_text() or ""
                f.write(text)
            except Exception as e:
                f.write(f"[extract error: {e}]")
    print("wrote", out, os.path.getsize(out))

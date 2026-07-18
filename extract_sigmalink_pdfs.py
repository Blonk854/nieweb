import pdfplumber, os, sys
pdfs = [
    "VIT_Sigmalink/1.6.5/Sigmalink-user-guide-V1.6.5.pdf",
    "VIT_Sigmalink/1.6.5/Sigmalink-release-note-V1.6.5.pdf",
    "VIT_Sigmalink/1.6.5/Analyse-user-guide-V1.6.5.pdf",
    "VIT_Sigmalink/1.6.5/Analyse-release-note-V1.6.5.pdf",
]
os.makedirs("pdf_text", exist_ok=True)
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

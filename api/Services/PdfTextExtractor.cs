using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace RagExample.Api.Services;

public static class PdfTextExtractor
{
    public static string ExtractText(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            // page.Text concatenates every glyph in raw content-stream order with no line
            // breaks, which mashes structured documents (resumes, tables, invoices) into one
            // run-on line - the reader then can't tell which date belongs to which job title.
            // ContentOrderTextExtractor does layout analysis and preserves line structure.
            sb.AppendLine(ContentOrderTextExtractor.GetText(page, true));
        }

        return sb.ToString();
    }
}

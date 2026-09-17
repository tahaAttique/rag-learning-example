using System.Text;
using UglyToad.PdfPig;

namespace RagExample.Api.Services;

public static class PdfTextExtractor
{
    public static string ExtractText(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }
}

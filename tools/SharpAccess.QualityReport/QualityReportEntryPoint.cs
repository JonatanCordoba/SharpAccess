using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SharpAccess.QualityReport;

internal static class QualityReportEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        int result = await Program.Main(args).ConfigureAwait(false);
        if (result != 0)
        {
            return result;
        }

        try
        {
            QualityReportPostProcessor.Apply(args);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

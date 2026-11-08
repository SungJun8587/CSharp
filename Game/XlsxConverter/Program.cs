using System;
using System.IO;
using System.Text;

namespace XlsxConverter
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 콘솔 출력은 BOM 없는 UTF-8이다. (한글 메시지가 빌드 로그와 파이프에서 깨지지 않게 한다)
            var utf8 = new UTF8Encoding(false);
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            using var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };

            return ConverterApp.Run(args, path => new ClosedXmlWorkbookReader(path), stdout, stderr);
        }
    }
}

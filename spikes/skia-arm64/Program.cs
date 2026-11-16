using System.Runtime.InteropServices;
using ScottPlot;

// Render a pie chart to PNG in memory, the way Phase 6 will send charts to Telegram
Plot plot = new();
plot.Add.Pie(new double[] { 5, 3, 2 });
byte[] png = plot.GetImageBytes(400, 300, ImageFormat.Png);

Console.WriteLine($"PNG bytes: {png.Length}");
Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");

byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
return png.Length > 0 && png.AsSpan().StartsWith(signature) ? 0 : 1;

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace StingTools.Core
{
    // ══════════════════════════════════════════════════════════════════════
    //  StingQRHelper — QR code generation helper
    //  Phase 76 Item 10
    //
    //  Uses ZXing.Net 0.16.9 (BarcodeWriterPixelData) to generate QR codes.
    //  Saves PNG via WPF PngBitmapEncoder — no System.Drawing.Common needed.
    // ══════════════════════════════════════════════════════════════════════

    public static class StingQRHelper
    {
        /// <summary>
        /// Generate a QR code as a WPF BitmapSource.
        /// </summary>
        /// <param name="content">The string to encode (URL, tag, or identifier).</param>
        /// <param name="size">Pixel width/height of the output image (default 200).</param>
        /// <returns>BitmapSource containing the QR code (can be used in WPF Image controls).</returns>
        public static BitmapSource GenerateQR(string content, int size = 200)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentNullException(nameof(content));

            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = size,
                    Height = size,
                    Margin = 1,
                    ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M
                }
            };

            var pixelData = writer.Write(content);

            // Create a WPF WriteableBitmap from raw pixel data (Bgra32)
            var bitmap = new WriteableBitmap(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(
                new Int32Rect(0, 0, pixelData.Width, pixelData.Height),
                pixelData.Pixels,
                pixelData.Width * 4,
                0);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Generate a QR code and save it as a PNG file.
        /// </summary>
        /// <param name="content">The string to encode.</param>
        /// <param name="outputPath">Full path for the output PNG file.</param>
        /// <param name="size">Pixel width/height (default 200).</param>
        /// <returns>The saved file path.</returns>
        public static string SaveQRPng(string content, string outputPath, int size = 200)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            var bitmapSource = GenerateQR(content, size);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using var stream = File.OpenWrite(outputPath);
            encoder.Save(stream);

            StingLog.Info($"StingQRHelper: saved QR code ({content.Length} chars) → {outputPath}");
            return outputPath;
        }

        /// <summary>
        /// Build the asset URL that a QR code should encode for a given element.
        /// Format: sting://asset/{projectCode}/{tagValue}
        /// </summary>
        public static string BuildAssetUrl(string projectCode, string tagValue)
        {
            var safe = Uri.EscapeDataString(tagValue ?? "");
            return $"sting://asset/{Uri.EscapeDataString(projectCode ?? "PRJ")}/{safe}";
        }
    }
}

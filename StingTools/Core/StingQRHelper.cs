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
                    // Margin is the quiet zone in MODULES. ISO/IEC 18004 requires 4;
                    // this was 1, which is out of spec and the usual cause of a code
                    // that reads on a screen and fails on a printed drawing where the
                    // surrounding linework encroaches on the finder patterns.
                    Margin = 4,
                    // Q (25% recovery) not M (15%): these get printed on site drawings
                    // and asset labels that get dusty, creased and photographed at an
                    // angle. The payload is short enough that the extra ECC costs a
                    // version or two, not legibility.
                    ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                    CharacterSet = "UTF-8"
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
            // File.Create, NOT File.OpenWrite. OpenWrite is OpenOrCreate and does not
            // truncate: regenerating over a longer existing PNG left the old file's
            // tail bytes past the new IEND chunk. Most decoders tolerate that, some
            // do not, and nothing warned either way.
            using var stream = File.Create(outputPath);
            encoder.Save(stream);

            StingLog.Info($"StingQRHelper: saved QR code ({content.Length} chars) → {outputPath}");
            return outputPath;
        }

        /// <summary>
        /// Build the URL a QR code should encode for a given element.
        ///
        /// Delegates to <see cref="StingQrFormat.BuildElementUrl"/>, which is the
        /// single statement of the format and is mirrored by the mobile parser.
        /// This wrapper exists only so existing call sites keep compiling.
        /// </summary>
        /// <remarks>
        /// It used to emit <c>sting://asset/{code}/{tag}</c>. Planscape's scanner
        /// never accepted that scheme, so every code produced by this method was
        /// unscannable by our own app — and unopenable by a stock phone camera,
        /// which is what a QR on a printed drawing is actually for. It now emits an
        /// https deep link. <see cref="StingQrFormat.Parse"/> still reads the legacy
        /// form so codes already printed on issued sheets keep resolving.
        /// </remarks>
        public static string BuildAssetUrl(string projectCode, string tagValue)
            => StingQrFormat.BuildElementUrl(projectCode, tagValue);
    }
}

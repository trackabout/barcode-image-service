using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace TrackAbout.BarcodeImageService
{
    public static class BarcodeImageService
    {
        /// <summary>
        /// Returns a Code-128 barcode in PNG format.
        /// </summary>
        /// <param name="req"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("barcode")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {

            // FYI, sizes are a little weird when rending QR codes. Setting to certain values seems to not "take"
            // until the value exceeds some magical boundary.
            // Explained here: https://stackoverflow.com/questions/17527591/zxing-net-qr-code-size
            // "Each Matrix code type has symmetrical representation requirements. It will always jump to an even number that is a multiple of the codeword size."

            // GS1 General Specification define that GS1-128 (the formal application of Code 128 to the supply chain industry) has a limits of 48 characters per symbol.
            // But technically, there's no limit as long as the device can read it.
            // Let's set our limit to a reasonable 100.
            const int maxValueLength = 2048;
            const string defaultImageWidth = "0";
            const string defaultImageHeight = "40";
            const int minImageWidth = 0;
            const int maxImageWidth = 2048;
            const int minImageHeight = 10;
            const int maxImageHeight = 2048;
            const string defaultMargin = "0";
            const int maxMargin = 200;
            
            // Validate the value, v
            if (!req.Query.ContainsKey("v"))
            {
                return new BadRequestObjectResult("No value 'v' specified. You must specify the value you wish to encode.");
            }
            string value = req.Query["v"];
            if (value.Length > maxValueLength)
            {
                return new BadRequestObjectResult($"Invalid length for value 'v'. This API will not render a barcode longer than {maxValueLength} characters.");
            }

            // Validate the output format 'fmt'.
            var outputFormat = req.Query.ContainsKey("fmt") ? req.Query["fmt"].ToString() : "png";
            if (outputFormat != "png" && outputFormat != "svg")
            {
                return new BadRequestObjectResult(
                    $"Invalid output file format 'fmt'. Value must be 'png' or 'svg'.");
            }

            // Validate the barcode symbology, 'sym'.
            var symbologyStr = req.Query.ContainsKey("sym") ? req.Query["sym"].ToString() : BarcodeFormat.CODE_128.ToString();
            var parsed = Enum.TryParse<BarcodeFormat>(symbologyStr, true, out var symbology);
            if (!parsed)
            {
                return new BadRequestObjectResult(
                    $"Invalid barcode symbology 'sym'. Value must be one of {string.Join(",", Enum.GetNames(typeof(BarcodeFormat)))}");
            }

            // Validate height, width and margin.
            var heightStr = req.Query.ContainsKey("h") ? req.Query["h"].ToString() : defaultImageHeight;
            var widthStr = req.Query.ContainsKey("w") ? req.Query["w"].ToString() : defaultImageWidth;
            var marginStr = req.Query.ContainsKey("m") ? req.Query["m"].ToString() : defaultMargin;

            int.TryParse(heightStr, out var height);
            if (height < minImageHeight || height > maxImageHeight)
            {
                return new BadRequestObjectResult($"Height 'h' must be between {minImageHeight} and {maxImageHeight}.");
            }
            int.TryParse(widthStr, out var width);
            if (width < minImageWidth || width > maxImageWidth)
            {
                return new BadRequestObjectResult($"Width 'w' must be between {minImageWidth} and {maxImageWidth}.");
            }

            int.TryParse(marginStr, out var margin);
            if (margin < 0 || margin > maxMargin)
            {
                return new BadRequestObjectResult($"Margin 'm' must be between 0 and {maxMargin}.");
            }

            // Configure the EncodingOptions.
            // There are many options for more complicated symbologies which we don't handle here.
            // Our purposes are focused on Code 128 and QR Code.
            var options = new EncodingOptions
            {
                Height = height
            };
            if (width > 0)
            {
                options.Width = width;
            }

            if (margin > 0)
            {
                options.Margin = margin;
            }
            

            // Branch depending on whether we're outputting a PNG graphics file or an SVG.
            if (outputFormat == "png")
            {
                var bcWriter = new BarcodeWriterPixelData
                {
                    Format = symbology,
                    Options = options
                };
                var pixelData = bcWriter.Write(value);
                using (var img = Image.LoadPixelData<Rgba32>(pixelData.Pixels, pixelData.Width, pixelData.Height))
                {
                    using (var ms = new MemoryStream())
                    {
                        img.SaveAsPng(ms);
                        return new FileContentResult(ms.ToArray(), "image/png");
                    }
                }
            }
            else // svg
            {
                var svgWriter = new BarcodeWriterSvg()
                {
                    Format = symbology,
                };
                var svg = svgWriter.Write(value);
                var bytes = Encoding.UTF8.GetBytes(svg.ToString());
                return new FileContentResult(bytes, "image/svg+xml");
            }
        }
    }
}
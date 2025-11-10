using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

            // Log the querystring.
            log.LogInformation($"querystring: {req.QueryString.Value}");
            
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

            var includeHumanReadable = req.Query.ContainsKey("ihr") && string.Compare(req.Query["ihr"].ToString(), bool.TrueString, true) == 0;

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
                    var barcode = img;

                    if (includeHumanReadable)
                        barcode = AddHumanReadable(img, value);

                    using (var ms = new MemoryStream())
                    {
                        barcode.SaveAsPng(ms);
                        return new FileContentResult(ms.ToArray(), "image/png");
                    }
                }
            }
            else // svg
            {
                var svgWriter = new BarcodeWriterSvg()
                {
                    Format = symbology,
                    Options = options
                };
                var svgContent = svgWriter.Write(value);
                byte[] bytes;

                // zxing.net already add the human readable for barcodes like Code 128.
                if (includeHumanReadable && string.Compare(symbologyStr, BarcodeFormat.QR_CODE.ToString(), true) == 0)
                {
                    var modifiedSvg = AddHumanReadable(svgContent.ToString(), value);
                    bytes = Encoding.UTF8.GetBytes(modifiedSvg);
                }
                else 
                    bytes = Encoding.UTF8.GetBytes(svgContent.ToString());

                return new FileContentResult(bytes, "image/svg+xml");
            }
        }

        /// <summary>
        /// Adds Human Readable below the barcode image. 
        /// Using SixLabors.ImageSharp is Azure Functions compatible (no GDI+ required).
        /// </summary>
        private static Image<Rgba32> AddHumanReadable(Image<Rgba32> barcodeImage, string text)
        {
            const int textHeight = 25;
            const int fontSize = 18;
            
            var newImage = new Image<Rgba32>(barcodeImage.Width, barcodeImage.Height + textHeight);

            newImage.Mutate(ctx => ctx.BackgroundColor(Color.White));
            newImage.Mutate(ctx => ctx.DrawImage(barcodeImage, new Point(0, 0), 1f));
            
            FontFamily fontFamily;
            if (!SystemFonts.TryGet("Arial", out fontFamily))
            {
                fontFamily = SystemFonts.Families.First();
            }
            var font = fontFamily.CreateFont(fontSize);
            
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(barcodeImage.Width / 2f, barcodeImage.Height + 5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
            
            newImage.Mutate(ctx => ctx.DrawText(textOptions, text, Color.Black));
            
            return newImage;
        }

        /// <summary>
        /// Adds Human Readable below the SVG barcode by manipulating the SVG XML content.
        /// </summary>
        private static string AddHumanReadable(string svgString, string text)
        {
            const int textHeight = 25;
            const int fontSize = 18;
            
            // Parse the viewBox to extract width and height
            var viewBoxMatch = System.Text.RegularExpressions.Regex.Match(
                svgString, 
                @"viewBox=""(\d+)\s+(\d+)\s+(\d+)\s+(\d+)""");
            
            if (!viewBoxMatch.Success)
                return svgString; // Return original if we can't parse viewBox
            
            int viewBoxX = int.Parse(viewBoxMatch.Groups[1].Value);
            int viewBoxY = int.Parse(viewBoxMatch.Groups[2].Value);
            int width = int.Parse(viewBoxMatch.Groups[3].Value);
            int height = int.Parse(viewBoxMatch.Groups[4].Value);
            int newHeight = height + textHeight;
            
            // Update the viewBox to accommodate the text
            var oldViewBox = $"viewBox=\"{viewBoxX} {viewBoxY} {width} {height}\"";
            var newViewBox = $"viewBox=\"{viewBoxX} {viewBoxY} {width} {newHeight}\"";
            svgString = svgString.Replace(oldViewBox, newViewBox);
            
            // Add text element before the closing </svg> tag
            var escapedText = System.Security.SecurityElement.Escape(text);
            var textElement = $"\n    <text x=\"{width / 2}\" y=\"{height + fontSize}\" " +
                             $"font-family=\"Arial, sans-serif\" font-size=\"{fontSize}\" " +
                             $"text-anchor=\"middle\" fill=\"rgb(0,0,0)\">{escapedText}</text>";
            
            svgString = svgString.Replace("</svg>", $"{textElement}\n</svg>");
            
            return svgString;
        }
    }
}
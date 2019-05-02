# TrackAbout Barcode Image Service Azure Function

This project deploys an Azure Function on .NET Core 2.0 which uses the ZXing library to render barcodes in various symbologies/formats and sizes.

## Querystring Arguments

Available querystring arguments are:

v
: The value you wish to encode as a barcode.

sym
: The barcode symbology (format). Must be one of the enum types defined by ZXing.BarcodeFormat. As of this writing, those types are: AZTEC, CODABAR, CODE_39, CODE_93, CODE_128, DATA_MATRIX, EAN_8, EAN_13, ITF, MAXICODE, PDF_417, QR_CODE, RSS_14, RSS_EXPANDED, UPC_A, UPC_E, All_1D, UPC_EAN_EXTENSION, MSI, PLESSEY, IMB

fmt
: The output file format. We currently support 'png' and 'svg'. Height and width are irrelevant for SVG. Not sure about margin.

h
: The height, in pixels, of the rendered barcode. For QR Code, there are certain jump-points between sizes that will cause a change in the outputted image size. Each Matrix code type has symmetrical representation requirements. It will always jump to an even number that is a multiple of the codeword size. 

w
: The width, in pixels, of the rendered barcode. For 1D barcodes (like Code 128), leave unset for default output.

m
: Margins, in pixels. For 1D barcodes, this modifies the left and right margins. Top and bottom are untouched.

## Known Limitations

### No printed values

This service cannot render the text of the value below the barcode.
This is a limitation of the Azure Functions platform, which does not allow use of routines that access the GDI graphics interface.
GDI is needed for font rendering. `System.Drawing` routines for rendering fonts are not available.

There's probably a workaround for this for the properly motivated developer.

### Only PNG or SVG
PNG generally produces the smallest file sizes for barcodes compared to JPEG, GIF and BMP.

SVGs scale up infinitely without blurring, but the file size (an XML document) tends to be larger than PNG.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using Microsoft.Kinect;
using ZXing;
using System.Windows;

namespace net.encausse.sarah {

  public class QRCodeMatcher {

    private BarcodeReader reader;
    private WriteableBitmap bitmap;
    private int colorW, colorH;

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    public QRCodeMatcher(int colorW, int colorH) {

      // Build Reader
      reader = new BarcodeReader {
        AutoRotate = true,
        TryHarder = true,
        PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
      };

      // Build WriteableBitmap
      this.colorW = colorW;
      this.colorH = colorH;
    }

    // ==========================================
    //  MATCHER
    // ==========================================

    byte[] buffer = null;
    byte[] flip   = null;
    public String CheckQRCode(byte[] data) {
      if (null == data) { return null; }
      if (null == buffer) { buffer = new byte[data.Length]; }
      if (null == flip)   { flip   = new byte[data.Length]; }

      Array.Copy(data, buffer, data.Length);
      int offset = 0;
      for (int y = 0; y < colorH; y++) {
        for (int x = 0; x < colorW; x++) {
          int nudge = (colorW - 1) * 4;
          flip[offset + 0 + x * 4] = buffer[offset + 0 - x * 4 + nudge];
          flip[offset + 1 + x * 4] = buffer[offset + 1 - x * 4 + nudge];
          flip[offset + 2 + x * 4] = buffer[offset + 2 - x * 4 + nudge];
          flip[offset + 3 + x * 4] = buffer[offset + 3 - x * 4 + nudge];
        }
        offset += 4 * colorW;
      }

      Result result = result = reader.Decode(flip, colorW, colorH, RGBLuminanceSource.BitmapFormat.BGRA32);
      if (result == null) { return null; }

      String type   = result.BarcodeFormat.ToString();
      String match = result.Text;
      return match;
    }
  }
}

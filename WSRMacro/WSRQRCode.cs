using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Microsoft.Kinect;
using ZXing;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace net.encausse.sarah {

  public class QRCodeManager {

    BarcodeReader reader = new BarcodeReader { 
      AutoRotate = true, 
      TryHarder = true, 
      PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE } 
    };

    // ==========================================
    //  QRCODE MANAGER
    // ==========================================

    private WSRKinectMacro wsr;
    
    public QRCodeManager(WSRKinectMacro wsr) {
      this.wsr = wsr;
    }

    private void fireQRCode(String match) {
      wsr.HandleQRCodeComplete(match);
    }

    // ==========================================
    //  COLOR FRAME
    // ==========================================

    public void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {
      CheckQRCode();
    }

    int threshold = 0;
    private WriteableBitmap bitmap;
    public void CheckQRCode() {
      if (threshold-- != 0) { return; } threshold = 24;

      // Build bitmap in current thread
      if (bitmap == null) {
        bitmap = wsr.NewColorBitmap();
      }

      Result result = null;
      using (Bitmap image = wsr.GetColorPNG(bitmap)) {
        result = reader.Decode(image);
      }
      
      if (result != null) {
        String type = result.BarcodeFormat.ToString();
        String match = result.Text;
        WSRConfig.GetInstance().logInfo("QRCODE", "Type: " + type + " Content: " + match);
        fireQRCode(match);
      }
    }
  }
}

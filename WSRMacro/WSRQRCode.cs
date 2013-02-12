using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Microsoft.Kinect;
using ZXing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

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

    private WriteableBitmap bitmap;

    private void fireQRCode(String match) {
      ((WSRKinectMacro)WSRMacro.GetInstance()).HandleQRCodeComplete(match);
    }

    public bool SetupQRCode() {
      if (WSRConfig.GetInstance().qrcode <= 0) {
        return false;
      }
      WSRConfig.GetInstance().logInfo("QRCODE", "Starting QRCode manager");
      return true;
    }

    // ==========================================
    //  COLOR FRAME
    // ==========================================

    Bitmap image = null;
    public void SensorColorFrameReady(object sender, ColorImageFrameReadyEventArgs e) {

      if (bitmap == null) {
        bitmap = ((WSRKinectMacro)WSRMacro.GetInstance()).NewColorBitmap();
      }

      CheckQRCode();
    }

    int threshold = 0;
    public void CheckQRCode() {
      if (threshold-- > 0) { return; } threshold = WSRConfig.GetInstance().qrcode;
      if (image != null) { return; }

      image = ((WSRKinectMacro)WSRMacro.GetInstance()).GetColorPNG(bitmap);
      Task.Factory.StartNew(() => {
        CheckQRCodeAsync(image);
        image.Dispose();
        image = null;
      });
    }

    public void CheckQRCodeAsync(Bitmap image) {

      Result result = result = reader.Decode(image);
      if (result == null) { return; }

      String type  = result.BarcodeFormat.ToString();
      String match = result.Text;
      WSRConfig.GetInstance().logInfo("QRCODE", "Type: " + type + " Content: " + match);
      fireQRCode(match);
    }
  }
}

using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

using Emgu.Util;
using Emgu.CV;
using Emgu.CV.UI;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;


namespace net.encausse.sarah {

  public class WSRFaceRecognition {

    // Singleton
    private static WSRFaceRecognition manager;
    public static WSRFaceRecognition GetInstance() {
      if (manager == null) {
        manager = new WSRFaceRecognition();
      }
      return manager;
    }

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    public void Setup() {
      InitHaarCascade();
      InitTrainedFaces();
    }

    private Dictionary<Rectangle, String> cachedResults = new Dictionary<Rectangle, String>();
    public Dictionary<Rectangle, String> GetResults() {
      return cachedResults;
    }

    // ==========================================
    //  DETECTION
    // ==========================================

    private HaarCascade haarCascade;
    private void InitHaarCascade() {
      haarCascade = new HaarCascade(@"Camera\haarcascade_frontalface_alt_tree.xml");
    }

    private Image<Gray, Byte> grayFrame;
    public void UpdateDetection(WriteableBitmap bitmap) {
      
      // Buid Color Frame
      var colorFrame = new Image<Bgr, Byte>(GetBitmapFromBitmapSource(bitmap));

      // Convert it to Grayscale
      grayFrame = colorFrame.Convert<Gray, Byte>();

      // Face Detection (where ?)
      MCvAvgComp[][] facesDetected = grayFrame.DetectHaarCascade(haarCascade, 1.2, 2,
      Emgu.CV.CvEnum.HAAR_DETECTION_TYPE.DO_CANNY_PRUNING, new System.Drawing.Size(80, 80));

      // Face Recognition
      cachedResults.Clear();
      foreach (MCvAvgComp f in facesDetected[0]) {
        if (f.rect == null) { continue; }
        String name = "Inconnu";
        if (recognizer != null) { // (who ?)
          Image<Gray, byte> result = grayFrame.Copy(f.rect).Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);
          name = recognizer.Recognize(result);
        }
        cachedResults.Add(f.rect, name);
      }

      // Send match
      if (cachedResults.Count > 0) {
        var matches = new List<String>(cachedResults.Values);
        ((WSRKinectMacro)WSRMacro.GetInstance()).HandleFaceComplete(matches);
      }
    }

    // ==========================================
    //  RECONGNITION
    // ==========================================

    private List<string> trainedLabels = new List<string>();
    private List<Image<Gray, byte>> trainedImages = new List<Image<Gray, byte>>();
    private void InitTrainedFaces() {
      DirectoryInfo dir = new DirectoryInfo(@"Camera\TrainedFaces\");
      foreach (FileInfo f in dir.GetFiles("*.bmp")) {
        var lbl = f.Name.Substring(0, f.Name.IndexOf("-"));
        var img = new Image<Gray, byte>(f.FullName);
        trainedLabels.Add(lbl);
        trainedImages.Add(img);
      }
      UpdateRecognizer();
    }

    private MCvTermCriteria termCrit;
    private EigenObjectRecognizer recognizer;
    private void UpdateRecognizer() {
      if (trainedImages.Count == 0) { return;  }

      // TermCriteria for face recognition with numbers of trained images like maxIteration
      termCrit = new MCvTermCriteria(trainedImages.Count, 0.001);

      // Eigen face recognizer
      recognizer = new EigenObjectRecognizer(trainedImages.ToArray(), trainedLabels.ToArray(), 5000, ref termCrit);
    }

    public BitmapSource TrainFace(String label) {

      // Get a gray frame from capture device
      Image<Gray, Byte> sample = grayFrame.Resize(320, 240, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);

      // Perform Face Detection
      MCvAvgComp[][] facesDetected = sample.DetectHaarCascade(haarCascade, 1.2, 10,
      Emgu.CV.CvEnum.HAAR_DETECTION_TYPE.DO_CANNY_PRUNING, new System.Drawing.Size(20, 20));

      // Retrieve trained face
      Image<Gray, byte> trainedFace = null;
      foreach (MCvAvgComp f in facesDetected[0]) {
        trainedFace = sample.Copy(f.rect).Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);
        break;
      }
      if (trainedFace == null) { return null; } // No face detected

      // Update trained memory
      label = Sanitize(label);
      trainedLabels.Add(label);
      trainedImages.Add(trainedFace);

      // Save to disk
      var Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      var now = (long)(DateTime.UtcNow - Jan1st1970).TotalSeconds;
      trainedFace.Save(@"Camera\TrainedFaces\" + label + "-" + now + ".bmp");

      // Update Recognizer
      UpdateRecognizer();

      return ToBitmapSource(trainedFace);
    }

    // ==========================================
    //  OPENCV - HELPER
    // ==========================================

    [DllImport("gdi32")]
    private static extern int DeleteObject(IntPtr o);

    public static Bitmap GetBitmapFromBitmapSource(BitmapSource bSource) {
      Bitmap bmp;
      using (MemoryStream ms = new MemoryStream()) {
        BitmapEncoder enc = new BmpBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bSource));
        enc.Save(ms);
        bmp = new Bitmap(ms);
      }
      return bmp;
    }

    public static BitmapSource ToBitmapSource(IImage image) {
      using (System.Drawing.Bitmap source = image.Bitmap) {
        IntPtr ptr = source.GetHbitmap(); //obtain the Hbitmap

        BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
            ptr,
            IntPtr.Zero,
            Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

        DeleteObject(ptr); // Release the HBitmap
        return bs;
      }
    }

    // ==========================================
    //  FILE - HELPER
    // ==========================================

    private static String invalidChars = System.Text.RegularExpressions.Regex.Escape(new String(System.IO.Path.GetInvalidFileNameChars()) + " -");
    private static String invalidReStr = String.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
    private static String Sanitize(String name) {
      return System.Text.RegularExpressions.Regex.Replace(name, invalidReStr, "_");
    }

  }
}

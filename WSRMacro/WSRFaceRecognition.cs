
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;

using Emgu.CV;
using Emgu.CV.UI;
using Emgu.CV.Structure;

namespace net.encausse.sarah {
  public class WSRFaceRecognition {

    public WSRFaceRecognition() {
      InitHaarCascade();
      InitTrainedFaces();
      InitEigenObjectRecognizer();
    }

    // ==========================================
    //  DETECTION
    // ==========================================

    private HaarCascade haarCascade;
    private void InitHaarCascade() {
      haarCascade = new HaarCascade(@"profile\haarcascade_frontalface_alt_tree.xml");
    }

    public String[]            Names  = new String[10];
    public Image<Gray, byte>[] Thumbs = new Image<Gray, byte>[10];
    public MCvAvgComp[]        Faces  = new MCvAvgComp[10];
    private Image<Gray, Byte>  Gray { get; set; }
    public void Detect(byte[] pixels, int width, int height) {

      // Build Image
      Bitmap bitmap = WSRKinectSensor.ToBitmap(pixels, width, height, PixelFormat.Format32bppRgb);
      Image<Bgr, Byte> color = new Image<Bgr, Byte>(bitmap);

      // Convert it to Grayscale
      Gray = color.Convert<Gray, Byte>();

      // Detect faces
      Faces = Gray.DetectHaarCascade(haarCascade, 1.2, 2, Emgu.CV.CvEnum.HAAR_DETECTION_TYPE.DO_CANNY_PRUNING, new Size(80, 80))[0];

      // Train if needed
      Train();
    }

    // ==========================================
    //  RECOGNITION
    // ==========================================

    private MCvTermCriteria termCrit;
    private EigenObjectRecognizer recognizer;
    private bool initEigen = false;
    private void InitEigenObjectRecognizer() {
      if (trainedImages.Count <= 0) { return; }
      initEigen = true;

      // TermCriteria for face recognition with numbers of trained images like maxIteration
      termCrit = new MCvTermCriteria(trainedImages.Count, 0.001);

      // Eigen face recognizer
      recognizer = new EigenObjectRecognizer(trainedImages.ToArray(), trainedLabels.ToArray(), 5000, ref termCrit);

      initEigen = false;
    }

    public String[] Recognize() {
      if (null == Faces || null == Gray || null == recognizer || initEigen) { return null; }
      Array.Clear(Names, 0, Names.Length);

      for (int i = 0; i < Faces.Length; i++) {
        MCvAvgComp f = Faces[i];
        if (null == f.rect) { continue; }

        // Build a thumbnail
        Thumbs[i] = Gray.Copy(f.rect).Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);

        // Recognize 
        Names[i] = recognizer.Recognize(Thumbs[i]);
      }
      return Names;
    }

    // ==========================================
    //  TRAINING
    // ==========================================

    private List<string> trainedLabels = new List<string>();
    private List<Image<Gray, byte>> trainedImages = new List<Image<Gray, byte>>();
    private void InitTrainedFaces() {
      DirectoryInfo dir = new DirectoryInfo(@"profile\faces\");
      foreach (FileInfo f in dir.GetFiles("*.bmp")) {
        var lbl = f.Name.Substring(0, f.Name.IndexOf("-"));
        var img = new Image<Gray, byte>(f.FullName);
        trainedLabels.Add(lbl);
        trainedImages.Add(img);
      }
    }

    private String train = null;
    public void TrainFace(String name) {
      this.train = name;
    }

    private void Train() {
      if (null == train || null == Gray || initEigen) { return; }

      // Get a gray frame from capture device
      Image<Gray, Byte> sample = Gray.Resize(320, 240, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);

      // Perform Face Detection
      MCvAvgComp[][] fd = sample.DetectHaarCascade(haarCascade, 1.2, 10,
      Emgu.CV.CvEnum.HAAR_DETECTION_TYPE.DO_CANNY_PRUNING, new System.Drawing.Size(20, 20));

      if (fd == null || fd.Length <= 0 || fd[0].Length <= 0) { return; }
      Image<Gray, byte> trainedFace = sample.Copy(fd[0][0].rect).Resize(100, 100, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);


      // Update trained memory
      String label = Sanitize(train);
      trainedLabels.Add(label);
      trainedImages.Add(trainedFace);
      train = null;

      // Save to disk
      var Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      var now = (long)(DateTime.UtcNow - Jan1st1970).TotalSeconds;
      trainedFace.Save(@"profile\faces\" + label + "-" + now + ".bmp");

      // Update Recognizer
      InitEigenObjectRecognizer();
    }

    // ==========================================
    //  HELPER
    // ==========================================

    private static String invalidChars = System.Text.RegularExpressions.Regex.Escape(new String(System.IO.Path.GetInvalidFileNameChars()) + " -");
    private static String invalidReStr = String.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
    private static String Sanitize(String name) {
      return System.Text.RegularExpressions.Regex.Replace(name, invalidReStr, "_");
    }
  }
}

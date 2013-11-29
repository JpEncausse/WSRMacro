using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit.FaceTracking; 

#if MICRO
using System.Speech.Recognition;
using System.Speech.AudioFormat;
#endif

#if KINECT
using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;
#endif

namespace net.encausse.sarah {

  public class WSRKinectSensor { 

    public KinectSensor            Sensor { get; set; }

    public CancellationTokenSource MotionSource  { get; set; }
    public CancellationTokenSource QRCodeSource  { get; set; }
    public CancellationTokenSource GestureSource { get; set; }
    public CancellationTokenSource ColorSource   { get; set; }
    public CancellationTokenSource FaceSource    { get; set; }

    private static readonly int Bgra32BytesPerPixel = (PixelFormats.Bgra32.BitsPerPixel / 8);
    private static readonly int Bgr32BytesPerPixel  = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

    // ==========================================
    //  CONSTRUCTOR
    // ==========================================

    public WSRKinectSensor(KinectSensor Sensor) { 
      this.Sensor = Sensor;
      Init();
    }

    public void Dispose() {
      if (Sensor == null) { return; }

      WSRConfig.GetInstance().logInfo("TASKS", "Stop Tasks");
      if (MotionSource != null)  MotionSource.Cancel();
      if (QRCodeSource != null)  QRCodeSource.Cancel();
      if (GestureSource != null) GestureSource.Cancel(); 
      if (ColorSource != null)   ColorSource.Cancel();
      if (FaceSource != null)    FaceSource.Cancel();

      if (Sensor == null) { return; }
      WSRConfig.GetInstance().logInfo("KINECT", "Stop Sensor");
      try {
        Sensor.AudioSource.Stop();
        Sensor.Stop();
      } catch(Exception){}
    }

    protected void Init() {

      // 1. Setup all Streams
      WSRConfig.GetInstance().logInfo("KINECT", "Setup sensor stream");
      try {

        // Gesture Seated
        if (WSRConfig.GetInstance().IsSeated) {
          Sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
        }

        TransformSmoothParameters smoothingParam = new TransformSmoothParameters();
        smoothingParam.Smoothing = 0.5f;
        smoothingParam.Correction = 0.5f;
        smoothingParam.Prediction = 0.5f;
        smoothingParam.JitterRadius = 0.05f;
        smoothingParam.MaxDeviationRadius = 0.04f;

        // Speech only
        if (!WSRConfig.GetInstance().SpeechOnly) {

          // Kinect for Windows
          // Sensor.DepthStream.Range = DepthRange.Near;
          // Sensor.SkeletonStream.EnableTrackingInNearRange = true;
          Sensor.SkeletonStream.Enable(smoothingParam);
          Sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
          Sensor.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);

          Sensor.AllFramesReady += KinectSensorOnAllFramesReady;
        }
      }
      catch (InvalidOperationException ex) { WSRConfig.GetInstance().logError("KINECT", ex); } // Device gone away just eat it.

      // 2. Start Sensor
      WSRConfig.GetInstance().logInfo("KINECT", "Start sensor");
      try { Sensor.Start(); }
      catch (IOException) {
        WSRConfig.GetInstance().logError("KINECT", "No Kinect Sensor: already used");
        Sensor = null;  // Some other application is streaming from the same Kinect sensor
        return;
      }
      
      // Elevation angle +/- 27
      Sensor.ElevationAngle = WSRConfig.GetInstance().SensorElevation;

      // 3. Start periodical tasks
      if (!WSRConfig.GetInstance().SpeechOnly) {
        WSRConfig.GetInstance().logInfo("TASKS", "Start tasks");
        FaceManager = new WSRFaceRecognition();
        var dueTime = TimeSpan.FromSeconds(5);

        QRCodeSource = new CancellationTokenSource();
        GestureSource = new CancellationTokenSource();
        ColorSource = new CancellationTokenSource();
        FaceSource = new CancellationTokenSource();
        MotionSource = new CancellationTokenSource();


        MotionAsync(dueTime, WSRConfig.GetInstance().Motion, MotionSource.Token);
        QRCodeAsync(dueTime, WSRConfig.GetInstance().QRCode, QRCodeSource.Token); // 35%
        GestureAsync(dueTime, WSRConfig.GetInstance().Gesture, GestureSource.Token);
        ColorAsync(dueTime, WSRConfig.GetInstance().Color, ColorSource.Token);
        FaceDetectAsync(dueTime, WSRConfig.GetInstance().FaceDetec, FaceSource.Token); // 7% (15% => 27% - 32%) 56%
        FaceRecognitionAsync(dueTime, WSRConfig.GetInstance().FaceReco, FaceSource.Token);
        FaceTrackingAsync(dueTime, WSRConfig.GetInstance().FaceTrack, FaceSource.Token);
      }
    }
    
    // ==========================================
    //  ALL FRAME READY
    // ==========================================

    public DateTime Timestamp            { get; set; }

    public DepthImagePixel[] DepthPixels { get; set; }
    public byte[] ColorData              { get; set; }
    public Skeleton[] Skeletons          { get; set; }
    public short[] DepthData             { get; set; }
    public int DepthMax, DepthMin;

    public bool CopyColorData   = false;
    public bool CopySkeletons   = false;
    public bool CopyDepthPixels = false;
    public bool Copy2DJoints    = false;

    public ColorImageFormat ColorFormat  { get; set; }
    public DepthImageFormat DepthFormat  { get; set; }

    public int ColorW { get; set; }
    public int ColorH { get; set; }
    public int DepthW { get; set; }
    public int DepthH { get; set; }
    public int fps = 0;

    public StopwatchAvg AllFrameWatch = new StopwatchAvg();
    private void KinectSensorOnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs) {

      if (++fps < WSRConfig.GetInstance().FPS) { return; } fps = 0;

      using (var depthFrame    = allFramesReadyEventArgs.OpenDepthImageFrame())
      using (var colorFrame    = allFramesReadyEventArgs.OpenColorImageFrame())
      using (var skeletonFrame = allFramesReadyEventArgs.OpenSkeletonFrame()) {

        if (null == depthFrame || null == colorFrame || null == skeletonFrame) {
          return;
        }

        AllFrameWatch.Again();

        // Depth Frame
        if (null == DepthPixels) {
          this.DepthFormat = depthFrame.Format;
          this.DepthW = depthFrame.Width;
          this.DepthH = depthFrame.Height;
          this.DepthMax = depthFrame.MaxDepth;
          this.DepthMin = depthFrame.MinDepth;
          this.DepthData = new short[depthFrame.PixelDataLength];
          this.DepthPixels = new DepthImagePixel[depthFrame.PixelDataLength];
        }
        
        // Color Frame
        if (null == ColorData) {
          this.ColorFormat = colorFrame.Format;
          this.ColorData = new byte[colorFrame.PixelDataLength];
          this.ColorW = colorFrame.Width;
          this.ColorH = colorFrame.Height; 
        }

        // Skeleton Frame
        if (null == Skeletons) {
          Skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
        }

        // Backup frames
        depthFrame.CopyPixelDataTo(this.DepthData);
        if (!StandBy) {
          this.Timestamp = System.DateTime.Now;
        }

        // Copy color data
        if (CopyColorData) {
          colorFrame.CopyPixelDataTo(this.ColorData);
          CopyColorData = false;

          // Remove transparency
          for (int i = 3; i < this.ColorData.Length; i += 4) { this.ColorData[i] = 255; }
        }

        // Copy skeleton data
        if (CopySkeletons) {
          skeletonFrame.CopySkeletonDataTo(this.Skeletons);
          CopySkeletons = false;
        }

        // Copy computed depth
        if (CopyDepthPixels) {
          depthFrame.CopyDepthImagePixelDataTo(this.DepthPixels);
          CopyDepthPixels = false;
        }

        // Convert Joint 3D to 2D on 1st skeleton
        if (Copy2DJoints){
          foreach (Skeleton sd in Skeletons) {
            if (sd.TrackingState != SkeletonTrackingState.Tracked) { continue;  }
            Map2DJoints(GestureManager.Skeleton, Sensor.CoordinateMapper);
            Copy2DJoints = false; break;
          }
        }

        AllFrameWatch.Stop();
      }
    }

    public Dictionary<JointType, System.Drawing.Point> Joints2D = new Dictionary<JointType, System.Drawing.Point>();
    protected void Map2DJoints(Skeleton sk, CoordinateMapper mapper) {
      if (null == sk) { return;  }
      foreach (Joint jt in sk.Joints) {
        var pts   = mapper.MapSkeletonPointToColorPoint(jt.Position, ColorFormat);
        Joints2D[jt.JointType] = new System.Drawing.Point(pts.X, pts.Y);
      }
    }

    // ==========================================
    //  TAKE PICTURE
    // ==========================================

    public String TakePicture(string folder) {

      Bitmap image = ToBitmap(ColorData, ColorW, ColorH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
      BitmapEncoder encoder = new JpegBitmapEncoder();
      String time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);
      String path = folder + "KinectSnapshot-" + time + ".jpg";
      using (FileStream fs = new FileStream(path, FileMode.Create)) {
        image.Save(fs, ImageFormat.Jpeg);
      }
      image.Dispose();

      WSRConfig.GetInstance().logInfo("PICTURE", "New picture to: " + path);
      return path;
    }

    // ==========================================
    //  STATIC BITMAP
    // ==========================================

    public static Bitmap ToBitmap(byte[] pixels, int width, int height, System.Drawing.Imaging.PixelFormat format) {
      if (pixels == null) { return null; }

      var bitmap = new Bitmap(width, height, format);
      var data = bitmap.LockBits(
          new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
          ImageLockMode.ReadWrite,
          bitmap.PixelFormat);

      Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
      bitmap.UnlockBits(data);

      return bitmap;
    }

    // ==========================================
    //  DEPTH PERIODICAL TASKS
    // ==========================================

    public StopwatchAvg MotionWatch = new StopwatchAvg();
    public int  Motion  { get; set; }
    public bool StandBy { get; set; }
    private async Task  MotionAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {

      if (interval.TotalMilliseconds == 0) return;

      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      DateTime LocalTimestamp = Timestamp; 
      short[] depth1 = new short[DepthData.Length];
      Array.Copy(DepthData, depth1, depth1.Length);
      Stopwatch StandByWatch = new Stopwatch();

      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {
        
        // Skip already work with given data
        // if (Timestamp == LocalTimestamp) {
        //  await Task.Delay(interval, token);
        //  continue;
        // }

        // Timestamp data
        // LocalTimestamp = Timestamp;
        MotionWatch.Again();

        // Do Job
        var tmp = StandBy;
        try {
          Motion = DepthManager.CompareDepth(depth1, DepthData);
          Array.Copy(DepthData, depth1, depth1.Length); // Backup
          if (Motion > WSRConfig.GetInstance().MotionTH) {
            StandByWatch.Restart();
            StandBy = false;
          }
          else if (StandByWatch.Elapsed > WSRConfig.GetInstance().StandBy) {
            StandByWatch.Stop();
            StandBy = true;
          }
          if (tmp != StandBy) {
            WSRHttpManager.GetInstance().SendRequest("http://127.0.0.1:8080/standby?motion=" + !StandBy);
          }
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("MOTION", ex);
        }
        MotionWatch.Stop();

        // Wait to repeat again.
        await Task.Delay(interval, token);
      }
    }

    // ==========================================
    //  QRCODE PERIODICAL TASKS
    // ==========================================

    public StopwatchAvg QRCodeWatch = new StopwatchAvg();
    private async Task  QRCodeAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {

      if (interval.TotalMilliseconds == 0) return;
      Stopwatch QRCodeTH = new Stopwatch();
      QRCodeTH.Start();

      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      DateTime LocalTimestamp = Timestamp; 
      QRCodeMatcher matcher   = new QRCodeMatcher(ColorW, ColorH);
      
      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Skip already work with given data
        if (Timestamp == LocalTimestamp) {
          await Task.Delay(interval, token);
          continue;
        }

        // Skip if skeleton Tracking
        if (null != GestureManager && null != GestureManager.Skeleton) {
          await Task.Delay(interval, token);
          continue;
        }

        // Timestamp data
        LocalTimestamp = Timestamp;
        QRCodeWatch.Again();

        // Do Job
        try {
          CopyColorData = true;
          String match = matcher.CheckQRCode(ColorData);
          if (match != null && QRCodeTH.Elapsed > WSRConfig.GetInstance().QRCodeTH) {
            WSRConfig.GetInstance().logInfo("QRCODE", "Match: " + match);
            WSRHttpManager.GetInstance().SendRequest(match);
            QRCodeTH.Restart();
          }
        }
        catch(Exception ex){
          WSRConfig.GetInstance().logError("QRCODE", ex);
        }
        QRCodeWatch.Stop();

        // Wait to repeat again.
        await Task.Delay(interval, token);
      }
    }

    // ==========================================
    //  GESTURE PERIODICAL TASKS
    // ==========================================

    public StopwatchAvg   GestureWatch = new StopwatchAvg();
    public GestureManager GestureManager = null;

    private async Task GestureAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {

      if (interval.TotalMilliseconds == 0)  return;
      Stopwatch GestureTH = new Stopwatch();
      GestureTH.Start();

      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      DateTime LocalTimestamp = Timestamp;
      
      if (null == GestureManager) {
        GestureManager = new GestureManager(this);
        GestureManager.Load();
      }

      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Skip already work with given data
        if (Timestamp == LocalTimestamp) {
          await Task.Delay(interval, token);
          continue;
        }

        if (WSRConfig.GetInstance().StandByGesture) {
          await Task.Delay(interval, token);
          continue;
        }

        // Timestamp data
        LocalTimestamp = Timestamp;
        GestureWatch.Again();

        // Do Job
        try {
          CopySkeletons = true;
          Gesture gesture = GestureManager.CheckGestures(Skeletons);
          if (null != gesture && GestureTH.Elapsed > WSRConfig.GetInstance().GestureTH) {
            WSRHttpManager.GetInstance().SendRequest(gesture.Url);
            GestureTH.Restart();
          }
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("GESTURE", ex);
        }
        GestureWatch.Stop();

        // Wait to repeat again.
        if (interval > TimeSpan.Zero)
          await Task.Delay(interval, token);
      }
    }

    // ==========================================
    //  COLOR PERIODICAL TASKS
    // ==========================================

    public StopwatchAvg ColorWatch = new StopwatchAvg();
    public RGB RGB { get; set; }
    private async Task ColorAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {

      if (interval.TotalMilliseconds == 0) return;
      Stopwatch ColorTH = new Stopwatch();
      ColorTH.Start();

      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);
      
      DateTime LocalTimestamp = Timestamp;
      WSRColor color = new WSRColor();
      
      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Skip already work with given data
        if (Timestamp == LocalTimestamp) {
          await Task.Delay(interval, token);
          continue;
        }

        // Timestamp data
        LocalTimestamp = Timestamp;
        ColorWatch.Again();

        // Do Job
        try {
          CopyColorData = true;
          var rgb = color.GetMostProminentColor(ColorData);
          if (RGB == null || rgb.r > 50 && rgb.g > 50 && rgb.b > 50) { RGB = rgb; }
          if (WSRConfig.GetInstance().ColorTH.Milliseconds > 0 && ColorTH.Elapsed > WSRConfig.GetInstance().ColorTH) {
            WSRHttpManager.GetInstance().SendRequest("http://127.0.01:8080/sarah/hue?r=" + RGB.r + "&g=" + RGB.g + "&b=" + RGB.b);
            ColorTH.Restart();
          }
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("COLOR", ex);
        }
        ColorWatch.Stop();

        // Wait to repeat again.
        if (interval > TimeSpan.Zero)
          await Task.Delay(interval, token);
      }
    }

    // ==========================================
    //  FACE PERIODICAL TASKS
    // ==========================================
    
    public StopwatchAvg FaceDetecWatch = new StopwatchAvg();
    public WSRFaceRecognition FaceManager { get; set; }
    private async Task FaceDetectAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {
      
      if (interval.TotalMilliseconds == 0) return;
      
      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      DateTime LocalTimestamp = Timestamp;

      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Skip already work with given data
        if (Timestamp == LocalTimestamp) {
          await Task.Delay(interval, token);
          continue;
        }

        // Timestamp data
        LocalTimestamp = Timestamp;
        FaceDetecWatch.Again();

        // Do Job
        try {
          CopyColorData = true;
          FaceManager.Detect(ColorData, ColorW, ColorH);
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("FACE", ex);
        }
        FaceDetecWatch.Stop();
          
        // Wait to repeat again.
        if (interval > TimeSpan.Zero)
          await Task.Delay(interval, token);
      }
    }

    //public Dictionary<String, DateTime> Faces = new Dictionary<String, DateTime>();
    public StopwatchAvg FaceRecoWatch = new StopwatchAvg();
    private async Task FaceRecognitionAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {

      if (interval.TotalMilliseconds == 0) return;
      TimeSpan threshold   = WSRConfig.GetInstance().FaceTH;

      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      DateTime LocalTimestamp = Timestamp;

      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Skip already work with given data
        if (Timestamp == LocalTimestamp) {
          await Task.Delay(interval, token);
          continue;
        }

        // Timestamp data
        LocalTimestamp = Timestamp;
        FaceRecoWatch.Again();

        // Do Job
        try {
          var names = FaceManager.Recognize();
          if (null != names) {
            WSRProfileManager.GetInstance().UpdateFace(names);
          }
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("FACE", ex);
        }
        FaceRecoWatch.Stop();

        // Wait to repeat again.
        if (interval > TimeSpan.Zero)
          await Task.Delay(interval, token);
      }
    }

    // ==========================================
    //  FACE TRACK PERIODICAL TASKS
    // ==========================================

    public float Mood = 0;
    public FaceTriangle[] FTriangles = null;
    public StopwatchAvg FaceTrackWatch = new StopwatchAvg();
    public EnumIndexableCollection<FeaturePoint, Microsoft.Kinect.Toolkit.FaceTracking.PointF> FPoints;
    private async Task FaceTrackingAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {
      if (interval.TotalMilliseconds == 0) return;

      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      DateTime LocalTimestamp = Timestamp;
      FaceTracker tracker = new FaceTracker(Sensor);

      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Skip already work with given data
        if (Timestamp == LocalTimestamp) {
          await Task.Delay(interval, token);
          continue;
        }

        // Timestamp data
        LocalTimestamp = Timestamp;
        FaceTrackWatch.Again();

        // Do Job
        try {
          CopyColorData = true;
          CopySkeletons = true;
          FPoints = null;
          Mood = 0;
          if (null != GestureManager && null != GestureManager.Skeleton) {
            FaceTrackFrame frame = tracker.Track(ColorFormat, ColorData, DepthFormat, DepthData, GestureManager.Skeleton);
            if (frame.TrackSuccessful) {
              
              // Only once.  It doesn't change.
              if (FTriangles == null) { FTriangles = frame.GetTriangles(); }
              FPoints = frame.GetProjected3DShape();
              Mood = frame.GetAnimationUnitCoefficients()[AnimationUnit.LipCornerDepressor];
              WSRProfileManager.GetInstance().UpdateMood(Mood);
            }
          }
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("FACE", ex);
        }
        FaceTrackWatch.Stop(); 

        // Wait to repeat again.
        if (interval > TimeSpan.Zero)
          await Task.Delay(interval, token);
      }

      // Dispose Tracker
      tracker.Dispose();
    }

  }
}

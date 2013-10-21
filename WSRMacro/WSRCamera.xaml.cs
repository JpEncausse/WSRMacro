using System;
using System.Globalization;
using System.IO;
using System.Drawing;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Microsoft.Kinect;

using Emgu.CV;
using Emgu.CV.Structure;

namespace net.encausse.sarah {
  
  public partial class WSRCamera : System.Windows.Window {

    // ==========================================
    //  STATIC
    // ==========================================
    public static object InitLock = new Object();

    public static Dictionary<String, WSRCamera> Cameras = new Dictionary<String, WSRCamera>();
    public static void Start(String name, WSRKinectSensor sensor) {
      Thread thread = new Thread(() => {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        WSRCamera camera = new WSRCamera(name, sensor);
        camera.Closing += (sender2, e2) => { camera.Hide(); e2.Cancel = true; };
        System.Windows.Threading.Dispatcher.Run();
      });

      thread.Name = name;
      thread.SetApartmentState(ApartmentState.STA);
      thread.IsBackground = true;
      thread.Start();
    }

    public static void Display(String name) {
      var camera = Cameras[name];
      if (camera == null) { return; }
      camera.Dispatcher.Invoke(new Action(() => camera.Show()));
    }

    public static void Shutdown() {
      foreach (WSRCamera camera in Cameras.Values) {
        if (null != camera.Source)
          camera.Source.Cancel();
        camera.Dispatcher.InvokeShutdown();
      }
    }

    private void WindowLoaded(object sender, System.Windows.RoutedEventArgs e) {}

    // ==========================================
    //  CONSTRCUTOR
    // ==========================================

    private CancellationTokenSource Source;
    private WSRKinectSensor Sensor;
    private WSRCamera(String name,  WSRKinectSensor sensor) {
      this.Sensor = sensor;
      this.Name = name;

      lock(InitLock){
        // Setup controls
        InitializeComponent();

        // Cleanup Thread
        Thread thread = Thread.CurrentThread;
        this.DataContext = new { ThreadId = thread.ManagedThreadId };

        // Store this to Cameras
        WSRConfig.GetInstance().logInfo("CAMERA", "Storing camera: " + name);
        Cameras.Add(name, this);
      }

      // Init BitMaps
      InitColorBitmap();
    }

    // ==========================================
    //  WINDOW
    // ==========================================

    protected bool visible = false;

    new public void Hide() {
      base.Hide();
      if (!visible) { return; }

      visible = false;
      if (null != Source) Source.Cancel();
    }

    new public void Show() {
      base.Show();
      if (visible) { return;  }
      visible = true;
      Source = new CancellationTokenSource();
      var dueTime = TimeSpan.FromMilliseconds(200);
      var interval = WSRConfig.GetInstance().FaceRepaint;
      RepaintAsync(dueTime, interval, Source.Token);
    }

    private void ButtonScreenshotClick(object sender, System.Windows.RoutedEventArgs e) {
      Sensor.FaceManager.TrainFace(this.textBox_Name.Text);
    }

    // ==========================================
    //  KINECT IMAGE
    // ==========================================

    private DepthFilteredSmoothing filter1;
    private DepthAveragedSmoothing filter2;

    private void InitColorBitmap() {
      WSRConfig cfg = WSRConfig.GetInstance();
      if (cfg.WSSmooth)  filter1 = new DepthFilteredSmoothing();
      if (cfg.WSAverage) filter2 = new DepthAveragedSmoothing();
    }

    // ==========================================
    //  BACKGROUND TASK
    // ==========================================
    private Bgr bGr = new Bgr(0, 255, 0);
    private Bgr bgR = new Bgr(0, 0, 255);
    private Bgr BGR = new Bgr(240, 240, 240);
    private SolidColorBrush red   = new SolidColorBrush(Colors.Red);
    private SolidColorBrush black = new SolidColorBrush(Colors.Black);
    private JointType[] jointInterest = new JointType[] { JointType.Head, JointType.ShoulderCenter, JointType.Spine, JointType.HipCenter, 
                                                          JointType.HipLeft, JointType.KneeLeft, JointType.AnkleLeft, JointType.FootLeft, 
                                                          JointType.HipRight, JointType.KneeRight, JointType.AnkleRight, JointType.FootRight,
                                                          JointType.ShoulderLeft, JointType.ElbowLeft, JointType.WristLeft, JointType.HandLeft,
                                                          JointType.ShoulderRight, JointType.ElbowRight, JointType.WristRight, JointType.HandRight
                                                        };

    public StopwatchAvg RepaintWatch = new StopwatchAvg();
    private async Task RepaintAsync(TimeSpan dueTime, TimeSpan interval, CancellationToken token) {
      // Initial wait time before we begin the periodic loop.
      if (dueTime > TimeSpan.Zero)
        await Task.Delay(dueTime, token);

      int ColorW = Sensor.ColorW;
      int ColorH = Sensor.ColorH;

      // Repeat this loop until cancelled.
      while (!token.IsCancellationRequested) {

        // Timestamp data
        RepaintWatch.Again();

        // Do Job
        try {
          // Draw Profile
          DrawProfile();

          // Draw Metrics
          DrawMetrics();

          if (!WSRConfig.GetInstance().SpeechOnly) {

            // Image color
            Sensor.CopyColorData = true;
            Bitmap bitmap = WSRKinectSensor.ToBitmap(Sensor.ColorData, ColorW, ColorH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            Image<Bgr, Byte> image = new Image<Bgr, Byte>(bitmap);

            DrawFaces(image);
            DrawJoints(image);
            DrawFace(image);
            DrawDepth(image);

            this.Image.Source = ToBitmapSource(image);

            // Prominent Color
            DrawProminentColor();
          }
        }
        catch (Exception ex) {
          WSRConfig.GetInstance().logError("CAMERA", ex);
        }
        RepaintWatch.Stop();

        // Wait to repeat again.
        if (interval > TimeSpan.Zero)
          await Task.Delay(interval, token);
      }
    }



    private void DrawProfile() {
      var current  = WSRProfileManager.GetInstance().Current;
      var profiles = WSRProfileManager.GetInstance().Profiles;
      for (int i = 0; i < profiles.Count && i < 5; i++) {
        WSRProfile profile = profiles[i];
        if (profile == null) { continue; }
        var label = ((TextBlock)System.Windows.LogicalTreeHelper.FindLogicalNode(this, "label_Profile" + i));
        label.Text = profile.Name;
        label.Foreground = (profile == current) ?  red : black;
        ((TextBlock)System.Windows.LogicalTreeHelper.FindLogicalNode(this, "label_Pitch" + i)).Text = "Pitch: "   + Math.Round(profile.Pitch);
        ((TextBlock)System.Windows.LogicalTreeHelper.FindLogicalNode(this, "label_Mood" + i)).Text = "Mood: "     + profile.Mood;
        ((TextBlock)System.Windows.LogicalTreeHelper.FindLogicalNode(this, "label_Height" + i)).Text = "Height: " + profile.Height;
        ((TextBlock)System.Windows.LogicalTreeHelper.FindLogicalNode(this, "label_Head" + i)).Text = "Head: "     + Math.Round(profile.x / 10)
                                                                                                            + "," + Math.Round(profile.y / 10)
                                                                                                            + "," + Math.Round(profile.z / 10);
      }

      if (null != current) {
        label_Gesture.Text = "Head: Tracked x: " + Math.Round(current.x / 10) + " y: " + Math.Round(current.y / 10) + " z: " + Math.Round(current.z / 10);
        label_Height.Text = "Height: " + current.Height + "m";
      }
    }

    private void DrawMetrics() {

      // Compute Motion
      this.label_Motion.Text = "Motion: " + Sensor.Motion + "%";
      this.label_Motion.Foreground = (Sensor.Motion > 5 ? red : black);

      // Set debug data
      this.AllFrameWatch.Text = "All Frame: " + Sensor.AllFrameWatch.Average();
      this.MotionWatch.Text = "Motion: " + Sensor.MotionWatch.Average();
      this.QRCodeWatch.Text = "QRCode: " + Sensor.QRCodeWatch.Average();
      this.GestureWatch.Text = "Gesture: " + Sensor.GestureWatch.Average();
      this.ColorWatch.Text = "Color: " + Sensor.ColorWatch.Average();
      this.FaceDetecWatch.Text = "Face Detec: " + Sensor.FaceDetecWatch.Average();
      this.FaceRecoWatch.Text = "Face Reco: " + Sensor.FaceRecoWatch.Average();
      this.FaceTrackWatch.Text = "Face Track: " + Sensor.FaceTrackWatch.Average();
      this.CamRepaintWatch.Text = "Camera Repaint: " + RepaintWatch.Average();
    }

    private Bgr border = new Bgr(0, 0, 255);
    private void DrawFaces(Image<Bgr, Byte> image) {
      MCvAvgComp[] Faces = Sensor.FaceManager.Faces;
      String[] Names = Sensor.FaceManager.Names;
      Image<Gray, byte>[] Thumbs = Sensor.FaceManager.Thumbs;
      for (int i = 0; i < Faces.Length; i++) {

        // Draw Rect
        MCvAvgComp f = Faces[i];
        if (f.rect == null) { continue; }
        image.Draw(f.rect, border, 2);

        // Draw text
        var rect = new Rectangle(f.rect.X, f.rect.Y + f.rect.Height + 20, f.rect.Width, f.rect.Height);
        DrawText(image, rect, Names[i] != null ? Names[i] : "");

        // Draw thumb
        if (Thumbs[i] != null) {
          this.imageTrainedFace.Source = ToBitmapSource(Thumbs[i]);
        }
      }
    }

    private void DrawJoints(Image<Bgr, Byte> image) {
      if (!Chx_Skeleton.IsChecked.GetValueOrDefault(true)) { return;  }
      if (null == Sensor.GestureManager) { return; }
      if (null == Sensor.GestureManager.Skeleton) { return; }

      Sensor.Copy2DJoints = true;
      var pts = Sensor.Joints2D;
      try {
        image.DrawPolyline(new Point[] { pts[JointType.Head], pts[JointType.ShoulderCenter], pts[JointType.Spine], pts[JointType.HipCenter] }, false, bGr, 2);
        image.DrawPolyline(new Point[] { pts[JointType.HipCenter], pts[JointType.HipLeft], pts[JointType.KneeLeft], pts[JointType.AnkleLeft], pts[JointType.FootLeft] }, false, bGr, 2);
        image.DrawPolyline(new Point[] { pts[JointType.HipCenter], pts[JointType.HipRight], pts[JointType.KneeRight], pts[JointType.AnkleRight], pts[JointType.FootRight] }, false, bGr, 2);
        image.DrawPolyline(new Point[] { pts[JointType.ShoulderCenter], pts[JointType.ShoulderLeft], pts[JointType.ElbowLeft], pts[JointType.WristLeft], pts[JointType.HandLeft] }, false, bGr, 2);
        image.DrawPolyline(new Point[] { pts[JointType.ShoulderCenter], pts[JointType.ShoulderRight], pts[JointType.ElbowRight], pts[JointType.WristRight], pts[JointType.HandRight] }, false, bGr, 2);
      }
      catch (Exception) { /* May have Exception accessing the map  */  }
      foreach (JointType t in jointInterest) {
        image.Draw(new CircleF(new PointF(pts[t].X, pts[t].Y), 1.0f), bgR, 10);
      }
    }

    private void DrawFace(Image<Bgr, Byte> image) {
      if (null == Sensor.FPoints) { return; }

      var pts = Sensor.FPoints;
      var color = Sensor.Mood > 0 ? bgR : Sensor.Mood < 0 ? bGr : BGR;
      foreach (var pt in pts) {
        image.Draw(new CircleF(new PointF(pt.X, pt.Y), 1.0f), color, 2);
      }
    }

    private void DrawDepth(Image<Bgr, Byte> image) {
      if (!Chx_Depth.IsChecked.GetValueOrDefault(true)) { return; }

      Parallel.For(0, Sensor.DepthH, y => {
        Parallel.For(0, Sensor.DepthW, x => {

          var intensity = (Sensor.DepthData[x + y * Sensor.DepthW] >> DepthImageFrame.PlayerIndexBitmaskWidth) * 255 / Sensor.DepthMax;
          image.Data[y, 320 + x, 0] = (byte)(intensity);
          image.Data[y, 320 + x, 1] = (byte)(intensity);
          image.Data[y, 320 + x, 2] = (byte)(intensity);

        });
      });
    }

    private void DrawProminentColor() {
      // Set Prominent color
      if (!Chx_Color.IsChecked.GetValueOrDefault(true)) {
        this.ProminentColor.Opacity = 0;
        return;
      }

      this.ProminentColor.Opacity = 100;
      RGB rgb = Sensor.RGB;
      if (null != rgb) {
        var color = System.Windows.Media.Color.FromRgb((byte)rgb.r, (byte)rgb.g, (byte)rgb.b);
        this.ProminentColor.Background = new SolidColorBrush(color);
      }
    }

    // ==========================================
    //  IMAGE - HELPER
    // ==========================================

    private Font font1 = new Font("Arial", 12, System.Drawing.FontStyle.Bold); //creates new font
    private void DrawText(Image<Bgr, Byte> img, Rectangle rect, string text) {
      Graphics g = Graphics.FromImage(img.Bitmap);

      int tWidth = (int)g.MeasureString(text, font1).Width;
      int x;
      if (tWidth >= rect.Width)
        x = rect.Left - ((tWidth - rect.Width) / 2);
      else
        x = (rect.Width / 2) - (tWidth / 2) + rect.Left;

      g.DrawString(text, font1, System.Drawing.Brushes.Red, new PointF(x, rect.Top - 18));
    }
    /*
    private Font font2 = new Font("Courrier New", 6, System.Drawing.FontStyle.Regular); //creates new font
    private void DrawText(Image<Bgr, Byte> img, PointF point, string text) {
      Graphics g = Graphics.FromImage(img.Bitmap);
      g.DrawString(text, font2, System.Drawing.Brushes.Red, point);
    }
    */
    /*
    private Font font2 = new Font("Arial", 11, System.Drawing.FontStyle.Regular);
    private void DrawText(Image<Bgr, Byte> img, Point p1, Point p2, string text) {
      if (null == p1 || null == p2) return;

      Graphics g = Graphics.FromImage(img.Bitmap);
      var angle = (float) Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
          angle = (float) ((angle/Math.PI*180) + (angle > 0 ? 0 : 360));

      int width  = (p2.X - p1.X);
      int height = (p2.Y - p1.Y);

      g.TranslateTransform(p1.X + width / 2, p1.Y + height / 2);
      g.RotateTransform(angle);
      g.DrawString(text, font2, System.Drawing.Brushes.Red, new PointF(0, 0));
      g.RotateTransform(-angle);
      g.TranslateTransform(-p1.X - width / 2, -p1.Y - height / 2);
    }*/

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
            System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

        DeleteObject(ptr); // Release the HBitmap
        return bs;
      }
    }
  }
}
